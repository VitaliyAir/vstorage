using VStorage.Domain.Common;
using VStorage.Domain.Events;

namespace VStorage.Domain.Buckets
{
    /// <summary>
    /// Bucket aggregate: name, single owner, members, quota. Enforces membership invariants;
    /// WHO may call an operation is decided with <see cref="BucketPermissions"/> by the application layer
    /// (the admin API bypasses the role checks, not these invariants).
    /// </summary>
    public sealed class Bucket : Entity
    {
        private readonly List<BucketMember> _members = [];

        private Bucket()
        {
        }

        private Bucket(Guid id, BucketName name, Guid ownerId, long? quotaBytes, DateTimeOffset now)
            : base(id)
        {
            Name = name;
            OwnerId = ownerId;
            QuotaBytes = quotaBytes;
            CreatedAt = now;
        }

        public BucketName Name { get; private set; } = null!;

        public Guid OwnerId { get; private set; }

        public long? QuotaBytes { get; private set; }

        /// <summary>Logical size of available objects. Changed atomically in SQL by uploads/deletes (stage S5).</summary>
        public long UsedBytes { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? DeletedAt { get; private set; }

        /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
        public uint Version { get; private set; }

        public IReadOnlyCollection<BucketMember> Members => _members;

        public bool IsDeleted => DeletedAt is not null;

        public static Result<Bucket> Create(BucketName name, Guid ownerId, long? quotaBytes, DateTimeOffset now)
        {
            if (quotaBytes is <= 0)
            {
                return BucketErrors.InvalidQuota;
            }

            var bucket = new Bucket(Guid.CreateVersion7(now), name, ownerId, quotaBytes, now);
            bucket.Raise(new BucketCreated(bucket.Id, name.Value, ownerId));
            return bucket;
        }

        /// <summary>The principal's role, or null when it has none (or the bucket is deleted).</summary>
        public BucketRole? RoleOf(Guid principalId)
        {
            if (IsDeleted)
            {
                return null;
            }

            return principalId == OwnerId ? BucketRole.Owner : FindMember(principalId)?.Role;
        }

        public Result AddMember(Guid userId, BucketRole role, Guid addedBy, DateTimeOffset now)
        {
            if (IsDeleted)
            {
                return BucketErrors.NotFound;
            }

            if (role == BucketRole.Owner)
            {
                return BucketErrors.InvalidMemberRole;
            }

            if (userId == OwnerId || FindMember(userId) is not null)
            {
                return BucketErrors.AlreadyMember;
            }

            _members.Add(new BucketMember(Id, userId, role, addedBy, now));
            Raise(new MemberAdded(Id, userId, role));
            return Result.Success();
        }

        public Result ChangeMemberRole(Guid userId, BucketRole role)
        {
            if (IsDeleted)
            {
                return BucketErrors.NotFound;
            }

            if (role == BucketRole.Owner)
            {
                return BucketErrors.InvalidMemberRole;
            }

            if (userId == OwnerId)
            {
                return BucketErrors.OwnerCannotBeChanged;
            }

            var member = FindMember(userId);
            if (member is null)
            {
                return BucketErrors.MemberNotFound;
            }

            if (member.Role != role)
            {
                var oldRole = member.Role;
                member.ChangeRole(role);
                Raise(new MemberRoleChanged(Id, userId, oldRole, role));
            }

            return Result.Success();
        }

        public Result RemoveMember(Guid userId) => Remove(userId, left: false);

        public Result Leave(Guid userId) => userId == OwnerId ? BucketErrors.OwnerCannotLeave : Remove(userId, left: true);

        /// <summary>
        /// Makes <paramref name="newOwnerId"/> the owner; the previous owner becomes admin.
        /// <paramref name="requireMembership"/> is true for the owner endpoint and false for the admin API.
        /// </summary>
        public Result TransferOwnership(Guid newOwnerId, bool requireMembership, DateTimeOffset now)
        {
            if (IsDeleted)
            {
                return BucketErrors.NotFound;
            }

            if (newOwnerId == OwnerId)
            {
                return BucketErrors.AlreadyOwner;
            }

            var member = FindMember(newOwnerId);
            if (member is null && requireMembership)
            {
                return BucketErrors.NewOwnerNotMember;
            }

            if (member is not null)
            {
                _members.Remove(member);
            }

            var previousOwnerId = OwnerId;
            OwnerId = newOwnerId;
            _members.Add(new BucketMember(Id, previousOwnerId, BucketRole.Admin, newOwnerId, now));
            Raise(new OwnershipTransferred(Id, previousOwnerId, newOwnerId));
            return Result.Success();
        }

        public Result SetQuota(long? quotaBytes)
        {
            if (IsDeleted)
            {
                return BucketErrors.NotFound;
            }

            if (quotaBytes is <= 0)
            {
                return BucketErrors.InvalidQuota;
            }

            QuotaBytes = quotaBytes;
            return Result.Success();
        }

        /// <summary>Pre-check before an upload; the authoritative check is the atomic SQL update when the upload commits.</summary>
        public bool HasRoomFor(long bytes) => QuotaBytes is null || UsedBytes + bytes <= QuotaBytes;

        /// <summary>Soft delete: the bucket disappears immediately; objects are purged after the retention period.</summary>
        public Result Delete(DateTimeOffset now)
        {
            if (IsDeleted)
            {
                return BucketErrors.NotFound;
            }

            DeletedAt = now;
            Raise(new BucketDeleted(Id));
            return Result.Success();
        }

        private BucketMember? FindMember(Guid userId) => _members.FirstOrDefault(m => m.UserId == userId);

        private Result Remove(Guid userId, bool left)
        {
            if (IsDeleted)
            {
                return BucketErrors.NotFound;
            }

            if (userId == OwnerId)
            {
                return BucketErrors.OwnerCannotBeChanged;
            }

            var member = FindMember(userId);
            if (member is null)
            {
                return BucketErrors.MemberNotFound;
            }

            _members.Remove(member);
            Raise(new MemberRemoved(Id, userId, left));
            return Result.Success();
        }
    }
}
