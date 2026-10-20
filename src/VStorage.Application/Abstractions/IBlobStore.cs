namespace VStorage.Application.Abstractions
{
    /// <summary>Size and SHA-256 of a written temp blob, or <see cref="TooLarge"/> when the limit was exceeded (nothing kept).</summary>
    public sealed record BlobWriteResult(long Size, string Sha256, bool TooLarge)
    {
        public static readonly BlobWriteResult Exceeded = new(0, "", true);
    }

    /// <summary>
    /// Port for blob bytes (spec §7.4). One immutable blob per object; the path is built from ids only.
    /// Write path: <see cref="WriteTempAsync"/> → <see cref="Commit"/> (atomic move into place).
    /// </summary>
    public interface IBlobStore
    {
        /// <summary>
        /// Streams <paramref name="content"/> to tmp/{objectId}, computing size and SHA-256 on the fly, and flushes it to disk.
        /// Stops and deletes the temp file as soon as more than <paramref name="maxBytes"/> arrive.
        /// </summary>
        Task<BlobWriteResult> WriteTempAsync(Guid objectId, Stream content, long maxBytes, CancellationToken cancellationToken);

        /// <summary>Atomically moves tmp/{objectId} to its final path in the bucket.</summary>
        void Commit(Guid bucketId, Guid objectId);

        /// <summary>Opens a committed blob for reading (seekable, for Range requests). Throws FileNotFoundException if missing.</summary>
        Stream OpenRead(Guid bucketId, Guid objectId);

        void DeleteTemp(Guid objectId);

        void Delete(Guid bucketId, Guid objectId);
    }
}
