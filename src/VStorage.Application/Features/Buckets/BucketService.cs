using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Principals;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;

namespace VStorage.Application.Features.Buckets
{
    /// <summary>Bucket create / read / quota / delete / transfer (spec §5.4) for the bucket API and the Admin API.</summary>
    public sealed class BucketService(IVStorageDbContext db, IClock clock)
    {
        /// <summary>
        /// Creates a bucket owned by <paramref name="ownerId"/>. Bucket API: owner = caller (endpoint requires
        /// bucket-creator). Admin API: any existing principal, e.g. a consumer's service account.
        /// </summary>
        public async Task<Result<BucketDto>> CreateAsync(
            BucketAccess access, Guid ownerId, string? name, long? quotaBytes, CancellationToken cancellationToken)
        {
            if (!access.AsPlatformAdmin && ownerId != access.Caller.Id)
            {
                return BucketAccessErrors.Forbidden;
            }

            var bucketName = BucketName.Create(name?.Trim());
            if (bucketName.IsFailure)
            {
                return bucketName.Error;
            }

            if (!await db.Principals.AnyAsync(p => p.Id == ownerId, cancellationToken))
            {
                return PrincipalErrors.UserNotFound;
            }

            if (await db.OwnerHasBucketNamedAsync(ownerId, bucketName.Value, cancellationToken))
            {
                return BucketErrors.NameTaken;
            }

            var created = Bucket.Create(bucketName.Value, ownerId, quotaBytes, clock.UtcNow);
            if (created.IsFailure)
            {
                return created.Error;
            }

            var bucket = created.Value;
            db.Buckets.Add(bucket);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A parallel request took the name first (unique index ux_buckets_owner_name).
                db.Buckets.Remove(bucket);
                if (await db.OwnerHasBucketNamedAsync(ownerId, bucketName.Value, cancellationToken))
                {
                    return BucketErrors.NameTaken;
                }

                throw;
            }

            return bucket.ToDto(access.Caller.Id);
        }

        /// <summary>Buckets where the caller is owner or member (FR-B3).</summary>
        public async Task<BucketDto[]> ListMineAsync(CurrentPrincipal caller, CancellationToken cancellationToken)
        {
            var buckets = await db.Buckets.AsNoTracking()
                .Include(b => b.Members)
                .Where(b => b.DeletedAt == null && (b.OwnerId == caller.Id || b.Members.Any(m => m.UserId == caller.Id)))
                .OrderBy(b => b.Name)
                .ToListAsync(cancellationToken);

            return buckets.Select(b => b.ToDto(caller.Id)).ToArray();
        }

        public async Task<Result<BucketDto>> GetAsync(BucketAccess access, Guid bucketId, CancellationToken cancellationToken)
        {
            var bucket = await db.LoadBucketForAsync(access, bucketId, BucketPermissions.CanRead, cancellationToken);
            if (bucket.IsFailure)
            {
                return bucket.Error;
            }

            return bucket.Value.ToDto(access.Caller.Id);
        }

        public async Task<Result<BucketDto>> SetQuotaAsync(BucketAccess access, Guid bucketId, long? quotaBytes, CancellationToken cancellationToken)
        {
            var loaded = await db.LoadBucketForAsync(access, bucketId, BucketPermissions.CanManageBucket, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded.Error;
            }

            var bucket = loaded.Value;
            var changed = bucket.SetQuota(quotaBytes);
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            var saved = await db.TrySaveAsync(cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            return bucket.ToDto(access.Caller.Id);
        }

        /// <summary>Soft delete (FR-B6): gone immediately; objects are purged by the cleanup worker (stage S9).</summary>
        public async Task<Result> DeleteAsync(BucketAccess access, Guid bucketId, CancellationToken cancellationToken)
        {
            var loaded = await db.LoadBucketForAsync(access, bucketId, BucketPermissions.CanManageBucket, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded.Error;
            }

            var deleted = loaded.Value.Delete(clock.UtcNow);
            return deleted.IsFailure ? deleted : await db.TrySaveAsync(cancellationToken);
        }

        /// <summary>
        /// FR-B7: the new owner must be a member (bucket API); the Admin API may hand the bucket to any existing
        /// principal. The previous owner becomes admin.
        /// </summary>
        public async Task<Result<BucketDto>> TransferAsync(BucketAccess access, Guid bucketId, Guid newOwnerId, CancellationToken cancellationToken)
        {
            var loaded = await db.LoadBucketForAsync(access, bucketId, BucketPermissions.CanManageBucket, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded.Error;
            }

            var bucket = loaded.Value;
            if (access.AsPlatformAdmin && !await db.Principals.AnyAsync(p => p.Id == newOwnerId, cancellationToken))
            {
                return PrincipalErrors.UserNotFound;
            }

            if (newOwnerId != bucket.OwnerId && await db.OwnerHasBucketNamedAsync(newOwnerId, bucket.Name, cancellationToken))
            {
                return BucketAccessErrors.NewOwnerHasBucketName;
            }

            var transferred = bucket.TransferOwnership(newOwnerId, requireMembership: !access.AsPlatformAdmin, clock.UtcNow);
            if (transferred.IsFailure)
            {
                return transferred.Error;
            }

            var saved = await db.TrySaveAsync(cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            return bucket.ToDto(access.Caller.Id);
        }
    }
}
