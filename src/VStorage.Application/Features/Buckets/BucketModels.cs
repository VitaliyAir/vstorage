using VStorage.Application.Abstractions;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;
using VStorage.Domain.Principals;

namespace VStorage.Application.Features.Buckets
{
    public sealed record CreateBucketRequest(string? Name, long? QuotaBytes);

    /// <param name="QuotaBytes">New quota in bytes; null = unlimited.</param>
    public sealed record UpdateBucketRequest(long? QuotaBytes);

    public sealed record TransferBucketRequest(Guid NewOwnerId);

    public sealed record AddMemberRequest(Guid UserId, BucketRole Role);

    public sealed record ChangeMemberRoleRequest(BucketRole Role);

    /// <param name="Role">The caller's own role in the bucket (null for a platform admin who is not a member).</param>
    /// <param name="MemberCount">Members besides the owner.</param>
    public sealed record BucketDto(
        Guid Id, string Name, Guid OwnerId, BucketRole? Role, long? QuotaBytes, long UsedBytes, int MemberCount, DateTimeOffset CreatedAt);

    public sealed record MemberDto(Guid UserId, string? DisplayName, PrincipalKind Kind, BucketRole Role, DateTimeOffset JoinedAt);

    /// <summary>
    /// Who manages a bucket: a principal with its own bucket role, or a platform admin (Admin API) who acts with
    /// owner rights for MANAGEMENT only. Object access never uses this (another user's private object stays invisible).
    /// </summary>
    public sealed record BucketAccess(CurrentPrincipal Caller, bool AsPlatformAdmin)
    {
        public static BucketAccess AsMember(CurrentPrincipal caller) => new(caller, false);

        public static BucketAccess AsAdmin(CurrentPrincipal caller) =>
            caller.IsPlatformAdmin ? new BucketAccess(caller, true) : throw new InvalidOperationException("The caller is not a platform admin.");

        public BucketRole? EffectiveRole(Bucket bucket)
        {
            if (bucket.IsDeleted)
            {
                return null;
            }

            return AsPlatformAdmin ? BucketRole.Owner : bucket.RoleOf(Caller.Id);
        }
    }

    public static class BucketAccessErrors
    {
        public static readonly Error Forbidden = Error.Forbidden("forbidden", "Your role in this bucket does not allow this action.");

        public static readonly Error ConcurrentUpdate = Error.Conflict("concurrent-update", "The bucket was changed by another request; reload and retry.");

        public static readonly Error NewOwnerHasBucketName = Error.Conflict("bucket-name-taken", "The new owner already owns a bucket with this name.");
    }
}
