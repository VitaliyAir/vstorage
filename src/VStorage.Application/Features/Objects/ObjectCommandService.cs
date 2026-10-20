using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Buckets;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;
using VStorage.Domain.Objects;

namespace VStorage.Application.Features.Objects
{
    /// <summary>Visibility change (FR-O12) and delete (FR-O13) of objects.</summary>
    public sealed class ObjectCommandService(IVStorageDbContext db, IClock clock)
    {
        /// <summary>Only the object's owner; private → shared when the shared key is free; shared → private is forbidden.</summary>
        public async Task<Result<ObjectDto>> ChangeVisibilityAsync(
            CurrentPrincipal caller, Guid objectId, ChangeVisibilityRequest request, CancellationToken cancellationToken)
        {
            var target = ObjectMapping.ParseVisibility(request.Visibility, fallback: null);
            if (target.IsFailure)
            {
                return target.Error;
            }

            if (target.Value is null)
            {
                return ObjectAccessErrors.InvalidVisibility;
            }

            var loaded = await LoadVisibleAsync(caller.Id, objectId, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded.Error;
            }

            var (storedObject, _) = loaded.Value;
            if (storedObject.OwnerId != caller.Id)
            {
                return ObjectAccessErrors.Forbidden;
            }

            if (storedObject.Visibility == target.Value)
            {
                return storedObject.ToDto();
            }

            if (target.Value == Visibility.Private)
            {
                return ObjectErrors.SharedToPrivateForbidden;
            }

            var sharedKeyTaken = await db.Objects.AnyAsync(
                o => o.BucketId == storedObject.BucketId
                    && o.Key == storedObject.Key
                    && o.Visibility == Visibility.Shared
                    && o.Status == ObjectStatus.Available,
                cancellationToken);
            if (sharedKeyTaken)
            {
                return ObjectErrors.KeyExists;
            }

            var shared = storedObject.MakeShared(clock.UtcNow);
            if (shared.IsFailure)
            {
                return shared.Error;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return ObjectErrors.KeyExists;
            }

            return storedObject.ToDto();
        }

        /// <summary>
        /// Soft delete: the object disappears immediately and its size is released from the quota in the same
        /// transaction; the blob is removed by the cleanup worker after the retention period (stage S9).
        /// Own objects: any role. Another user's shared object: owner/admin of the bucket.
        /// </summary>
        public async Task<Result> DeleteAsync(CurrentPrincipal caller, Guid objectId, CancellationToken cancellationToken)
        {
            var loaded = await LoadVisibleAsync(caller.Id, objectId, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded.Error;
            }

            var (storedObject, role) = loaded.Value;
            if (storedObject.OwnerId != caller.Id && !BucketPermissions.CanManageOthersSharedObjects(role))
            {
                return ObjectAccessErrors.Forbidden;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var deleted = storedObject.Delete(clock.UtcNow);
            if (deleted.IsFailure)
            {
                return deleted;
            }

            await db.TryChangeAsync(storedObject.BucketId, -(storedObject.Size ?? 0), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success();
        }

        // Tracked object that the caller may see, plus the caller's bucket role.
        private async Task<Result<(StoredObject Object, BucketRole Role)>> LoadVisibleAsync(
            Guid callerId, Guid objectId, CancellationToken cancellationToken)
        {
            var storedObject = await db.Objects
                .SingleOrDefaultAsync(o => o.Id == objectId && o.Status == ObjectStatus.Available, cancellationToken);
            if (storedObject is null || !storedObject.IsVisibleTo(callerId))
            {
                return ObjectErrors.NotFound;
            }

            var bucket = await db.FindBucketAsync(storedObject.BucketId, cancellationToken);
            if (bucket?.RoleOf(callerId) is not BucketRole role)
            {
                return ObjectErrors.NotFound;
            }

            return (storedObject, role);
        }
    }
}
