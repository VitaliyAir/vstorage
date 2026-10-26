using VStorage.Domain.Buckets;
using VStorage.Domain.Common;
using VStorage.Domain.Objects;
using VStorage.Domain.Sharing;

namespace VStorage.Domain.Events
{
    public sealed record BucketCreated(Guid BucketId, string Name, Guid OwnerId) : IDomainEvent;

    public sealed record BucketDeleted(Guid BucketId) : IDomainEvent;

    public sealed record MemberAdded(Guid BucketId, Guid UserId, BucketRole Role) : IDomainEvent;

    public sealed record MemberRoleChanged(Guid BucketId, Guid UserId, BucketRole OldRole, BucketRole NewRole) : IDomainEvent;

    /// <param name="Left">True when the member left on their own, false when removed by owner/admin.</param>
    public sealed record MemberRemoved(Guid BucketId, Guid UserId, bool Left) : IDomainEvent;

    public sealed record OwnershipTransferred(Guid BucketId, Guid PreviousOwnerId, Guid NewOwnerId) : IDomainEvent;

    public sealed record ObjectCreated(Guid ObjectId, Guid BucketId, string Key, Guid OwnerId, Visibility Visibility, long Size, string Sha256)
        : IDomainEvent;

    public sealed record ObjectDeleted(Guid ObjectId, Guid BucketId, Guid OwnerId) : IDomainEvent;

    public sealed record ObjectVisibilityChanged(Guid ObjectId, Guid BucketId, Visibility Visibility) : IDomainEvent;

    public sealed record ObjectRenamed(Guid ObjectId, Guid BucketId, string OldKey, string NewKey) : IDomainEvent;

    public sealed record ShareLinkCreated(Guid ShareLinkId, Guid ObjectId, ShareLinkKind Kind, Guid CreatedBy) : IDomainEvent;

    /// <param name="PrincipalId">The signed-in recipient of an addressed link; null for a public link.</param>
    public sealed record ShareLinkRedeemed(Guid ShareLinkId, Guid ObjectId, Guid? PrincipalId) : IDomainEvent;

    public sealed record ShareLinkRevoked(Guid ShareLinkId, Guid ObjectId) : IDomainEvent;
}
