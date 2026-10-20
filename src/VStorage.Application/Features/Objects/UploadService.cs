using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Buckets;
using VStorage.Application.Options;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;
using VStorage.Domain.Objects;

namespace VStorage.Application.Features.Objects
{
    /// <summary>
    /// Streaming upload (spec §5.6, §7.3): pending row → temp blob (size + SHA-256 on the fly, limits enforced while
    /// streaming) → atomic move → one transaction that applies FR-O6, the quota and makes the object available.
    /// Anything left behind by a crash stays "pending" and is removed by reconciliation (stage S9).
    /// </summary>
    public sealed class UploadService(
        IVStorageDbContext db, IBlobStore blobs, IClock clock, IOptions<StorageOptions> storageOptions, ILogger<UploadService> logger)
    {
        public async Task<Result<UploadOutcome>> UploadAsync(CurrentPrincipal caller, UploadCommand command, CancellationToken cancellationToken)
        {
            var key = ObjectKey.Create(command.Key);
            if (key.IsFailure)
            {
                return key.Error;
            }

            var parsedVisibility = ObjectMapping.ParseVisibility(command.Visibility, Visibility.Private);
            if (parsedVisibility.IsFailure)
            {
                return parsedVisibility.Error;
            }

            var visibility = parsedVisibility.Value!.Value;

            var bucket = await db.FindBucketAsync(command.BucketId, cancellationToken);
            var role = bucket?.RoleOf(caller.Id);
            var access = BucketStore.Check(role, BucketPermissions.CanUpload(role));
            if (access.IsFailure)
            {
                return access.Error;
            }

            // Checked before any byte is read: preconditions, overwrite rights, size and quota.
            var current = await CurrentObjects(bucket!.Id, key.Value, visibility, caller.Id).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            var precondition = CheckPrecondition(command.Precondition, current);
            if (precondition.IsFailure)
            {
                return precondition.Error;
            }

            var overwrites = IsOverwrite(command.Precondition);
            if (overwrites && current is not null && current.OwnerId != caller.Id && !BucketPermissions.CanManageOthersSharedObjects(role))
            {
                return ObjectAccessErrors.Forbidden;
            }

            var (limit, limitedByQuota) = UploadLimit(bucket, overwrites ? current?.Size : null);
            if (command.DeclaredLength > limit)
            {
                return LimitError(limitedByQuota);
            }

            var begun = StoredObject.BeginUpload(
                bucket.Id, key.Value, caller.Id, visibility, command.ContentType, command.FileName, clock.UtcNow);
            if (begun.IsFailure)
            {
                return begun.Error;
            }

            var storedObject = begun.Value;
            db.Objects.Add(storedObject);
            await db.SaveChangesAsync(cancellationToken);

            BlobWriteResult written;
            try
            {
                written = await blobs.WriteTempAsync(storedObject.Id, command.Content, limit, cancellationToken);
                if (!written.TooLarge)
                {
                    blobs.Commit(bucket.Id, storedObject.Id);
                }
            }
            catch
            {
                await DiscardAsync(bucket.Id, storedObject.Id);
                throw;
            }

            if (written.TooLarge)
            {
                await DiscardAsync(bucket.Id, storedObject.Id);
                return LimitError(limitedByQuota);
            }

            return await FinishAsync(caller, bucket.Id, key.Value, visibility, command.Precondition, storedObject, written, cancellationToken);
        }

        private async Task<Result<UploadOutcome>> FinishAsync(
            CurrentPrincipal caller,
            Guid bucketId,
            ObjectKey key,
            Visibility visibility,
            UploadPrecondition precondition,
            StoredObject storedObject,
            BlobWriteResult written,
            CancellationToken cancellationToken)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Re-read the key inside the transaction: another upload may have finished meanwhile.
            var current = await CurrentObjects(bucketId, key, visibility, caller.Id).SingleOrDefaultAsync(cancellationToken);
            var stillValid = CheckPrecondition(precondition, current);
            if (stillValid.IsFailure)
            {
                return await AbortAsync(transaction, bucketId, storedObject.Id, stillValid.Error);
            }

            if (current is not null && !IsOverwrite(precondition))
            {
                // FR-O6 without precondition: identical content → 200 with the existing object, different → 409.
                Error? conflict = current.Sha256 == written.Sha256 ? null : ObjectErrors.KeyExists;
                var existing = current.ToDto();
                await AbortAsync(transaction, bucketId, storedObject.Id, conflict);
                return conflict is null ? new UploadOutcome(existing, Created: false) : conflict;
            }

            var replaced = current;
            if (!await db.TryChangeAsync(bucketId, written.Size - (replaced?.Size ?? 0), cancellationToken))
            {
                return await AbortAsync(transaction, bucketId, storedObject.Id, BucketErrors.QuotaExceeded);
            }

            var now = clock.UtcNow;
            replaced?.Delete(now);
            var completed = storedObject.Complete(written.Size, written.Sha256, now);
            if (completed.IsFailure)
            {
                return await AbortAsync(transaction, bucketId, storedObject.Id, completed.Error);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The partial unique index caught a parallel upload of the same key.
                return await AbortAsync(transaction, bucketId, storedObject.Id, ObjectErrors.KeyExists);
            }

            return new UploadOutcome(storedObject.ToDto(), Created: replaced is null);
        }

        private IQueryable<StoredObject> CurrentObjects(Guid bucketId, ObjectKey key, Visibility visibility, Guid callerId) =>
            db.Objects.Where(o =>
                o.BucketId == bucketId
                && o.Key == key
                && o.Status == ObjectStatus.Available
                && o.Visibility == visibility
                && (visibility == Visibility.Shared || o.OwnerId == callerId));

        private static bool IsOverwrite(UploadPrecondition precondition) =>
            precondition.Kind is UploadPreconditionKind.IfMatchAny or UploadPreconditionKind.IfMatchETag;

        private static Result CheckPrecondition(UploadPrecondition precondition, StoredObject? current)
        {
            var met = precondition.Kind switch
            {
                UploadPreconditionKind.IfNoneMatchAny => current is null,
                UploadPreconditionKind.IfMatchAny => current is not null,
                UploadPreconditionKind.IfMatchETag => current is not null && current.Sha256 == precondition.ETag,
                _ => true
            };
            return met ? Result.Success() : ObjectAccessErrors.PreconditionFailed;
        }

        // The smaller of MaxUploadBytes and the free quota (an overwrite gives back the replaced object's size).
        private (long Limit, bool LimitedByQuota) UploadLimit(Bucket bucket, long? replacedSize)
        {
            var maxUpload = storageOptions.Value.MaxUploadBytes;
            if (bucket.QuotaBytes is not long quota)
            {
                return (maxUpload, false);
            }

            var free = Math.Max(0, quota - bucket.UsedBytes + (replacedSize ?? 0));
            return free < maxUpload ? (free, true) : (maxUpload, false);
        }

        private Error LimitError(bool limitedByQuota) =>
            limitedByQuota ? BucketErrors.QuotaExceeded : ObjectAccessErrors.TooLarge(storageOptions.Value.MaxUploadBytes);

        private async Task<Result<UploadOutcome>> AbortAsync(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, Guid bucketId, Guid objectId, Error? error)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await DiscardAsync(bucketId, objectId);
            return error ?? ObjectErrors.KeyExists;
        }

        // Best effort: delete the bytes and mark the pending row deleted. If this fails too, reconciliation cleans up.
        private async Task DiscardAsync(Guid bucketId, Guid objectId)
        {
            try
            {
                blobs.DeleteTemp(objectId);
                blobs.Delete(bucketId, objectId);
                db.ChangeTracker.Clear();
                var pending = await db.Objects.SingleOrDefaultAsync(o => o.Id == objectId, CancellationToken.None);
                if (pending is not null)
                {
                    pending.Abandon(clock.UtcNow);
                    await db.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not discard upload {ObjectId}; reconciliation will remove it", objectId);
            }
        }
    }
}
