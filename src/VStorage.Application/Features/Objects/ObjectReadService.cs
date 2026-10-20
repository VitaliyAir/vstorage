using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Buckets;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;
using VStorage.Domain.Objects;

namespace VStorage.Application.Features.Objects
{
    /// <summary>
    /// Metadata and downloads (FR-O7..O9, FR-O14). Visible = bucket member AND (shared OR own private);
    /// everything else is 404, including for the bucket owner and platform admins.
    /// </summary>
    public sealed class ObjectReadService(IVStorageDbContext db, IBlobStore blobs, ILogger<ObjectReadService> logger)
    {
        public async Task<Result<ObjectDto>> GetMetadataAsync(CurrentPrincipal caller, Guid objectId, CancellationToken cancellationToken)
        {
            var storedObject = await LoadVisibleAsync(caller.Id, objectId, cancellationToken);
            return storedObject.IsSuccess ? storedObject.Value.ToDto() : storedObject.Error;
        }

        public Task<Result<ObjectContent>> OpenByIdAsync(CurrentPrincipal caller, Guid objectId, CancellationToken cancellationToken) =>
            OpenByIdForAsync(caller.Id, objectId, cancellationToken);

        /// <summary>Same rules for a principal that is not the caller: the creator of a download link (FR-D4).</summary>
        public async Task<Result<ObjectContent>> OpenByIdForAsync(Guid principalId, Guid objectId, CancellationToken cancellationToken)
        {
            var storedObject = await LoadVisibleAsync(principalId, objectId, cancellationToken);
            return storedObject.IsSuccess ? Open(storedObject.Value) : storedObject.Error;
        }

        /// <summary>
        /// Share links (FR-S5): an available object in a live bucket, without a principal check - the caller has checked
        /// the link.
        /// </summary>
        public async Task<Result<ObjectContent>> OpenAvailableAsync(Guid objectId, CancellationToken cancellationToken)
        {
            var storedObject = await db.Objects.AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == objectId && o.Status == ObjectStatus.Available, cancellationToken);
            if (storedObject is null || await db.FindBucketAsync(storedObject.BucketId, cancellationToken) is null)
            {
                return ObjectErrors.NotFound;
            }

            return Open(storedObject);
        }

        /// <summary>FR-O7: without <paramref name="visibility"/> the caller's own private object wins over the shared one.</summary>
        public async Task<Result<ObjectContent>> OpenByKeyAsync(
            CurrentPrincipal caller, Guid bucketId, string? key, string? visibility, CancellationToken cancellationToken)
        {
            var objectKey = ObjectKey.Create(key);
            if (objectKey.IsFailure)
            {
                return objectKey.Error;
            }

            var requested = ObjectMapping.ParseVisibility(visibility, fallback: null);
            if (requested.IsFailure)
            {
                return requested.Error;
            }

            var bucket = await db.FindBucketAsync(bucketId, cancellationToken);
            if (!BucketPermissions.CanRead(bucket?.RoleOf(caller.Id)))
            {
                return ObjectErrors.NotFound;
            }

            var candidates = await db.Objects.AsNoTracking()
                .Where(o => o.BucketId == bucketId
                    && o.Key == objectKey.Value
                    && o.Status == ObjectStatus.Available
                    && (o.Visibility == Visibility.Shared || o.OwnerId == caller.Id))
                .ToListAsync(cancellationToken);

            var match = candidates
                .Where(o => requested.Value is null || o.Visibility == requested.Value)
                .OrderBy(o => o.Visibility == Visibility.Private ? 0 : 1)
                .FirstOrDefault();

            return match is null ? ObjectErrors.NotFound : Open(match);
        }

        private async Task<Result<StoredObject>> LoadVisibleAsync(Guid callerId, Guid objectId, CancellationToken cancellationToken)
        {
            var storedObject = await db.Objects.AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == objectId && o.Status == ObjectStatus.Available, cancellationToken);
            if (storedObject is null || !storedObject.IsVisibleTo(callerId))
            {
                return ObjectErrors.NotFound;
            }

            var bucket = await db.FindBucketAsync(storedObject.BucketId, cancellationToken);
            if (!BucketPermissions.CanRead(bucket?.RoleOf(callerId)))
            {
                return ObjectErrors.NotFound;
            }

            return storedObject;
        }

        private Result<ObjectContent> Open(StoredObject storedObject)
        {
            try
            {
                return new ObjectContent(storedObject.ToDto(), blobs.OpenRead(storedObject.BucketId, storedObject.Id));
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                logger.LogError("Blob of available object {ObjectId} in bucket {BucketId} is missing", storedObject.Id, storedObject.BucketId);
                return ObjectErrors.NotFound;
            }
        }
    }
}
