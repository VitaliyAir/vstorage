using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Buckets;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;
using VStorage.Domain.Objects;

namespace VStorage.Application.Features.Objects
{
    /// <summary>Visibility change (FR-O12), delete (FR-O13) and key rename of objects.</summary>
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

        /// <summary>
        /// New keys for several of the caller's own objects in one transaction (all or nothing), e.g. when a client
        /// renames or moves a folder of its own tree. Ids that are not available objects of the caller are skipped.
        /// </summary>
        public async Task<Result<RenameObjectsResult>> RenameAsync(
            CurrentPrincipal caller, Guid bucketId, RenameObjectsRequest request, CancellationToken cancellationToken)
        {
            var items = request.Items ?? [];
            if (items.Count is 0 or > ObjectAccessErrors.MaxRenameItems)
            {
                return ObjectAccessErrors.RenameCount;
            }

            var newKeys = new Dictionary<Guid, ObjectKey>();
            foreach (var item in items)
            {
                var key = ObjectKey.Create(item.Key);
                if (key.IsFailure)
                {
                    return key.Error;
                }

                if (!newKeys.TryAdd(item.ObjectId, key.Value))
                {
                    return ObjectAccessErrors.RenameDuplicate;
                }
            }

            if (newKeys.Values.Distinct().Count() != newKeys.Count)
            {
                return ObjectAccessErrors.RenameDuplicate;
            }

            var bucket = await db.FindBucketAsync(bucketId, cancellationToken);
            var role = bucket?.RoleOf(caller.Id);
            var access = BucketStore.Check(role, BucketPermissions.CanUpload(role));
            if (access.IsFailure)
            {
                return access.Error;
            }

            var ids = newKeys.Keys.ToList();
            var objects = await db.Objects
                .Where(o => o.BucketId == bucketId && ids.Contains(o.Id) && o.OwnerId == caller.Id && o.Status == ObjectStatus.Available)
                .ToListAsync(cancellationToken);

            // A new key must be free in its scope (own private objects / shared objects), apart from objects of this request.
            var targetKeys = objects.Select(o => newKeys[o.Id]).ToList();
            var holders = await db.Objects.AsNoTracking()
                .Where(o => o.BucketId == bucketId
                    && o.Status == ObjectStatus.Available
                    && !ids.Contains(o.Id)
                    && targetKeys.Contains(o.Key)
                    && (o.Visibility == Visibility.Shared || o.OwnerId == caller.Id))
                .Select(o => new { o.Key, o.Visibility })
                .ToListAsync(cancellationToken);
            var taken = objects
                .Where(o => holders.Any(h => h.Key == newKeys[o.Id] && h.Visibility == o.Visibility))
                .Select(o => newKeys[o.Id].Value)
                .ToList();
            if (taken.Count > 0)
            {
                return ObjectAccessErrors.RenameKeysTaken(taken);
            }

            var renamed = 0;
            var now = clock.UtcNow;
            foreach (var storedObject in objects)
            {
                var result = storedObject.Rename(newKeys[storedObject.Id], now);
                if (result.IsFailure)
                {
                    return result.Error;
                }

                renamed += result.Value ? 1 : 0;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Unique key index: e.g. two objects of the request swapping keys, or a parallel upload took a key.
                db.ChangeTracker.Clear();
                return ObjectErrors.KeyExists;
            }

            return new RenameObjectsResult(renamed, objects.Count - renamed, ids.Except(objects.Select(o => o.Id)).ToList());
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
