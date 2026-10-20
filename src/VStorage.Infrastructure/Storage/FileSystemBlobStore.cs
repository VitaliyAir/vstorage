using System.Buffers;
using System.Security.Cryptography;
using VStorage.Application.Abstractions;

namespace VStorage.Infrastructure.Storage
{
    /// <summary>Local-disk blob store: streamed writes with on-the-fly SHA-256, fsync, atomic move into place.</summary>
    internal sealed class FileSystemBlobStore(BlobPaths paths) : IBlobStore
    {
        private const int BufferSize = 81_920;

        public async Task<BlobWriteResult> WriteTempAsync(Guid objectId, Stream content, long maxBytes, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(paths.TempDirectory);
            var path = paths.Temp(objectId);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long size = 0;
                var tooLarge = false;

                await using (var file = new FileStream(
                    path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    int read;
                    while ((read = await content.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
                    {
                        size += read;
                        if (size > maxBytes)
                        {
                            tooLarge = true;
                            break;
                        }

                        hash.AppendData(buffer, 0, read);
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }

                    if (!tooLarge)
                    {
                        await file.FlushAsync(cancellationToken);
                        file.Flush(flushToDisk: true);
                    }
                }

                if (tooLarge)
                {
                    DeleteFile(path);
                    return BlobWriteResult.Exceeded;
                }

                return new BlobWriteResult(size, Convert.ToHexStringLower(hash.GetHashAndReset()), TooLarge: false);
            }
            catch
            {
                DeleteFile(path);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void Commit(Guid bucketId, Guid objectId)
        {
            var target = paths.Object(bucketId, objectId);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(paths.Temp(objectId), target, overwrite: false);
        }

        public Stream OpenRead(Guid bucketId, Guid objectId) =>
            new FileStream(
                paths.Object(bucketId, objectId),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

        public void DeleteTemp(Guid objectId) => DeleteFile(paths.Temp(objectId));

        public void Delete(Guid bucketId, Guid objectId) => DeleteFile(paths.Object(bucketId, objectId));

        private static void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
