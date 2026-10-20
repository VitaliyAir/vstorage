using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Principals;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;

namespace VStorage.Application.Features.Buckets
{
    /// <summary>
    /// Bucket members (spec §5.5). Only existing principals can be added (FR-M1).
    /// Side effects of a removal (private objects, share links - FR-M6) are handled by the worker in stage S9.
    /// </summary>
    public sealed class MemberService(IVStorageDbContext db, IClock clock)
    {
        public async Task<Result<MemberDto[]>> ListAsync(BucketAccess access, Guid bucketId, CancellationToken cancellationToken)
        {
            var bucket = await db.LoadBucketForAsync(access, bucketId, BucketPermissions.CanRead, cancellationToken);
            if (bucket.IsFailure)
            {
                return bucket.Error;
            }

            return await ToMemberDtosAsync(bucket.Value, cancellationToken);
        }

        public async Task<Result<MemberDto>> AddAsync(BucketAccess access, Guid bucketId, AddMemberRequest request, CancellationToken cancellationToken)
        {
            if (!IsMemberRole(request.Role))
            {
                return BucketErrors.InvalidMemberRole;
            }

            var loaded = await db.LoadBucketForAsync(
                access, bucketId, role => BucketPermissions.CanManageMember(role, request.Role), cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded.Error;
            }

            if (!await db.Principals.AnyAsync(p => p.Id == request.UserId, cancellationToken))
            {
                return PrincipalErrors.UserNotFound;
            }

            var bucket = loaded.Value;
            var added = bucket.AddMember(request.UserId, request.Role, access.Caller.Id, clock.UtcNow);
            if (added.IsFailure)
            {
                return added.Error;
            }

            return await SaveAndDescribeAsync(bucket, request.UserId, cancellationToken);
        }

        public async Task<Result<MemberDto>> ChangeRoleAsync(
            BucketAccess access, Guid bucketId, Guid userId, ChangeMemberRoleRequest request, CancellationToken cancellationToken)
        {
            if (!IsMemberRole(request.Role))
            {
                return BucketErrors.InvalidMemberRole;
            }

            var target = await LoadTargetAsync(access, bucketId, userId, cancellationToken);
            if (target.IsFailure)
            {
                return target.Error;
            }

            var (bucket, callerRole, targetRole) = target.Value;
            var check = BucketStore.Check(
                callerRole,
                BucketPermissions.CanManageMember(callerRole, targetRole) && BucketPermissions.CanManageMember(callerRole, request.Role));
            if (check.IsFailure)
            {
                return check.Error;
            }

            var changed = bucket.ChangeMemberRole(userId, request.Role);
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            return await SaveAndDescribeAsync(bucket, userId, cancellationToken);
        }

        /// <summary>Removes a member; removing yourself through the bucket API is the same as leaving.</summary>
        public async Task<Result> RemoveAsync(BucketAccess access, Guid bucketId, Guid userId, CancellationToken cancellationToken)
        {
            if (!access.AsPlatformAdmin && userId == access.Caller.Id)
            {
                return await LeaveAsync(access.Caller, bucketId, cancellationToken);
            }

            var target = await LoadTargetAsync(access, bucketId, userId, cancellationToken);
            if (target.IsFailure)
            {
                return target.Error;
            }

            var (bucket, callerRole, targetRole) = target.Value;
            var check = BucketStore.Check(callerRole, BucketPermissions.CanManageMember(callerRole, targetRole));
            if (check.IsFailure)
            {
                return check;
            }

            var removed = bucket.RemoveMember(userId);
            return removed.IsFailure ? removed : await db.TrySaveAsync(cancellationToken);
        }

        /// <summary>FR-M5: a member leaves the bucket; the owner must transfer the ownership first.</summary>
        public async Task<Result> LeaveAsync(CurrentPrincipal caller, Guid bucketId, CancellationToken cancellationToken)
        {
            var bucket = await db.FindBucketAsync(bucketId, cancellationToken);
            if (bucket?.RoleOf(caller.Id) is null)
            {
                return BucketErrors.NotFound;
            }

            var left = bucket.Leave(caller.Id);
            return left.IsFailure ? left : await db.TrySaveAsync(cancellationToken);
        }

        private static bool IsMemberRole(BucketRole role) => Enum.IsDefined(role) && role != BucketRole.Owner;

        // Loads the bucket for a caller with a role, and the target's current role (owner cannot be touched).
        private async Task<Result<(Bucket Bucket, BucketRole CallerRole, BucketRole TargetRole)>> LoadTargetAsync(
            BucketAccess access, Guid bucketId, Guid userId, CancellationToken cancellationToken)
        {
            var bucket = await db.FindBucketAsync(bucketId, cancellationToken);
            var callerRole = bucket is null ? null : access.EffectiveRole(bucket);
            if (bucket is null || callerRole is null)
            {
                return BucketErrors.NotFound;
            }

            var targetRole = bucket.RoleOf(userId);
            if (targetRole is null)
            {
                return BucketErrors.MemberNotFound;
            }

            if (targetRole == BucketRole.Owner)
            {
                return BucketErrors.OwnerCannotBeChanged;
            }

            return (bucket, callerRole.Value, targetRole.Value);
        }

        private async Task<Result<MemberDto>> SaveAndDescribeAsync(Bucket bucket, Guid userId, CancellationToken cancellationToken)
        {
            var saved = await db.TrySaveAsync(cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            var members = await ToMemberDtosAsync(bucket, cancellationToken);
            return members.Single(m => m.UserId == userId);
        }

        // Owner first, then by role (admin, member, viewer) and name.
        private async Task<MemberDto[]> ToMemberDtosAsync(Bucket bucket, CancellationToken cancellationToken)
        {
            var userIds = bucket.Members.Select(m => m.UserId).Append(bucket.OwnerId).ToList();
            var principals = await db.Principals.AsNoTracking()
                .Where(p => userIds.Contains(p.Id))
                .Select(p => new { p.Id, p.DisplayName, p.Kind })
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var owner = principals[bucket.OwnerId];
            var members = bucket.Members.Select(m =>
            {
                var principal = principals[m.UserId];
                return new MemberDto(m.UserId, principal.DisplayName, principal.Kind, m.Role, m.JoinedAt);
            });

            return members
                .Prepend(new MemberDto(bucket.OwnerId, owner.DisplayName, owner.Kind, BucketRole.Owner, bucket.CreatedAt))
                .OrderByDescending(m => m.Role)
                .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
