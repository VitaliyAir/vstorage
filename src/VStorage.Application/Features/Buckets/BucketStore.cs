using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;

namespace VStorage.Application.Features.Buckets
{
    /// <summary>Loading, access checks and saving shared by the bucket, member and admin services.</summary>
    internal static class BucketStore
    {
        public static Task<Bucket?> FindBucketAsync(this IVStorageDbContext db, Guid bucketId, CancellationToken cancellationToken) =>
            db.Buckets.Include(b => b.Members).SingleOrDefaultAsync(b => b.Id == bucketId && b.DeletedAt == null, cancellationToken);

        /// <summary>
        /// Loads a live bucket and checks <paramref name="permission"/> for the caller's effective role:
        /// no role → 404 (the bucket's existence is not revealed), a role without the right → 403.
        /// </summary>
        public static async Task<Result<Bucket>> LoadBucketForAsync(
            this IVStorageDbContext db, BucketAccess access, Guid bucketId, Func<BucketRole?, bool> permission, CancellationToken cancellationToken)
        {
            var bucket = await db.FindBucketAsync(bucketId, cancellationToken);
            var role = bucket is null ? null : access.EffectiveRole(bucket);
            var check = Check(role, role is not null && permission(role));
            if (check.IsFailure)
            {
                return check.Error;
            }

            return bucket!;
        }

        public static Result Check(BucketRole? role, bool allowed)
        {
            if (role is null)
            {
                return BucketErrors.NotFound;
            }

            return allowed ? Result.Success() : BucketAccessErrors.Forbidden;
        }

        /// <summary>SaveChanges with the optimistic concurrency conflict (xmin) turned into a 409.</summary>
        public static async Task<Result> TrySaveAsync(this IVStorageDbContext db, CancellationToken cancellationToken)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (DbUpdateConcurrencyException)
            {
                return BucketAccessErrors.ConcurrentUpdate;
            }
        }

        public static Task<bool> OwnerHasBucketNamedAsync(this IVStorageDbContext db, Guid ownerId, BucketName name, CancellationToken cancellationToken) =>
            db.Buckets.AnyAsync(b => b.OwnerId == ownerId && b.Name == name && b.DeletedAt == null, cancellationToken);

        public static BucketDto ToDto(this Bucket bucket, Guid viewerId) =>
            new(bucket.Id, bucket.Name.Value, bucket.OwnerId, bucket.RoleOf(viewerId), bucket.QuotaBytes, bucket.UsedBytes, bucket.Members.Count, bucket.CreatedAt);
    }
}
