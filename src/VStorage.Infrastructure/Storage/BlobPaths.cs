using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VStorage.Application.Options;

namespace VStorage.Infrastructure.Storage
{
    /// <summary>
    /// Disk layout (spec §7.1), built from ids only - never from object keys:
    ///   {BlobRoot}/tmp/{objectId}
    ///   {BlobRoot}/objects/{bucketId}/{id[^2..]}/{id[^4..^2]}/{objectId}
    /// The last hex characters of the UUIDv7 are used because its first characters are a timestamp.
    /// tmp/ and objects/ share one root, so the final move is an atomic rename on the same file system.
    /// </summary>
    public sealed class BlobPaths
    {
        public BlobPaths(IOptions<StorageOptions> options, IHostEnvironment environment)
        {
            Root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.BlobRoot));
        }

        public string Root { get; }

        public string TempDirectory => Path.Combine(Root, "tmp");

        public string ObjectsDirectory => Path.Combine(Root, "objects");

        public string Temp(Guid objectId) => Path.Combine(TempDirectory, objectId.ToString("N"));

        public string BucketDirectory(Guid bucketId) => Path.Combine(ObjectsDirectory, bucketId.ToString("N"));

        public string Object(Guid bucketId, Guid objectId)
        {
            var id = objectId.ToString("N");
            return Path.Combine(BucketDirectory(bucketId), id[^2..], id[^4..^2], id);
        }
    }
}
