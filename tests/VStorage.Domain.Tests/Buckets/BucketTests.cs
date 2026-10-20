using VStorage.Domain.Buckets;
using VStorage.Domain.Events;

namespace VStorage.Domain.Tests.Buckets
{
    public sealed class BucketTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        private static readonly Guid Owner = Guid.NewGuid();
        private static readonly Guid UserA = Guid.NewGuid();
        private static readonly Guid UserB = Guid.NewGuid();

        [Fact]
        public void Create_sets_owner_and_raises_BucketCreated()
        {
            var bucket = NewBucket();

            bucket.RoleOf(Owner).Should().Be(BucketRole.Owner);
            bucket.RoleOf(UserA).Should().BeNull();
            bucket.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<BucketCreated>();
        }

        [Fact]
        public void Create_rejects_non_positive_quota()
        {
            Bucket.Create(BucketName.Create("abc").Value, Owner, 0, Now).Error.Should().Be(BucketErrors.InvalidQuota);
        }

        [Fact]
        public void AddMember_gives_the_role_once()
        {
            var bucket = NewBucket();

            bucket.AddMember(UserA, BucketRole.Member, Owner, Now).IsSuccess.Should().BeTrue();

            bucket.RoleOf(UserA).Should().Be(BucketRole.Member);
            bucket.AddMember(UserA, BucketRole.Viewer, Owner, Now).Error.Should().Be(BucketErrors.AlreadyMember);
            bucket.AddMember(Owner, BucketRole.Admin, Owner, Now).Error.Should().Be(BucketErrors.AlreadyMember);
            bucket.AddMember(UserB, BucketRole.Owner, Owner, Now).Error.Should().Be(BucketErrors.InvalidMemberRole);
        }

        [Fact]
        public void ChangeMemberRole_raises_event_only_on_change()
        {
            var bucket = NewBucket();
            bucket.AddMember(UserA, BucketRole.Member, Owner, Now);
            bucket.ClearDomainEvents();

            bucket.ChangeMemberRole(UserA, BucketRole.Member).IsSuccess.Should().BeTrue();
            bucket.DomainEvents.Should().BeEmpty();

            bucket.ChangeMemberRole(UserA, BucketRole.Admin).IsSuccess.Should().BeTrue();
            bucket.RoleOf(UserA).Should().Be(BucketRole.Admin);
            bucket.DomainEvents.Should().ContainSingle().Which.Should().Be(new MemberRoleChanged(bucket.Id, UserA, BucketRole.Member, BucketRole.Admin));
        }

        [Fact]
        public void Owner_cannot_be_removed_demoted_or_leave()
        {
            var bucket = NewBucket();

            bucket.RemoveMember(Owner).Error.Should().Be(BucketErrors.OwnerCannotBeChanged);
            bucket.ChangeMemberRole(Owner, BucketRole.Admin).Error.Should().Be(BucketErrors.OwnerCannotBeChanged);
            bucket.Leave(Owner).Error.Should().Be(BucketErrors.OwnerCannotLeave);
        }

        [Fact]
        public void Leave_and_remove_raise_MemberRemoved_with_reason()
        {
            var bucket = NewBucket();
            bucket.AddMember(UserA, BucketRole.Member, Owner, Now);
            bucket.AddMember(UserB, BucketRole.Viewer, Owner, Now);
            bucket.ClearDomainEvents();

            bucket.Leave(UserA).IsSuccess.Should().BeTrue();
            bucket.RemoveMember(UserB).IsSuccess.Should().BeTrue();

            bucket.DomainEvents.Should().Equal(new MemberRemoved(bucket.Id, UserA, true), new MemberRemoved(bucket.Id, UserB, false));
            bucket.RemoveMember(UserA).Error.Should().Be(BucketErrors.MemberNotFound);
        }

        [Fact]
        public void Transfer_requires_membership_and_makes_previous_owner_admin()
        {
            var bucket = NewBucket();

            bucket.TransferOwnership(UserA, requireMembership: true, Now).Error.Should().Be(BucketErrors.NewOwnerNotMember);

            bucket.AddMember(UserA, BucketRole.Member, Owner, Now);
            bucket.TransferOwnership(UserA, requireMembership: true, Now).IsSuccess.Should().BeTrue();

            bucket.OwnerId.Should().Be(UserA);
            bucket.RoleOf(UserA).Should().Be(BucketRole.Owner);
            bucket.RoleOf(Owner).Should().Be(BucketRole.Admin);
            bucket.Members.Should().ContainSingle(m => m.UserId == Owner);
        }

        [Fact]
        public void Admin_api_transfer_works_without_membership()
        {
            var bucket = NewBucket();

            bucket.TransferOwnership(UserB, requireMembership: false, Now).IsSuccess.Should().BeTrue();

            bucket.OwnerId.Should().Be(UserB);
            bucket.TransferOwnership(UserB, requireMembership: false, Now).Error.Should().Be(BucketErrors.AlreadyOwner);
        }

        [Fact]
        public void Deleted_bucket_gives_no_roles_and_rejects_changes()
        {
            var bucket = NewBucket();
            bucket.AddMember(UserA, BucketRole.Member, Owner, Now);

            bucket.Delete(Now).IsSuccess.Should().BeTrue();

            bucket.RoleOf(Owner).Should().BeNull();
            bucket.RoleOf(UserA).Should().BeNull();
            bucket.AddMember(UserB, BucketRole.Member, Owner, Now).Error.Should().Be(BucketErrors.NotFound);
            bucket.Delete(Now).Error.Should().Be(BucketErrors.NotFound);
        }

        [Fact]
        public void Quota_check()
        {
            var bucket = Bucket.Create(BucketName.Create("abc").Value, Owner, 100, Now).Value;

            bucket.HasRoomFor(100).Should().BeTrue();
            bucket.HasRoomFor(101).Should().BeFalse();
            bucket.SetQuota(null).IsSuccess.Should().BeTrue();
            bucket.HasRoomFor(long.MaxValue).Should().BeTrue();
            bucket.SetQuota(-1).Error.Should().Be(BucketErrors.InvalidQuota);
        }

        private static Bucket NewBucket() => Bucket.Create(BucketName.Create("test-bucket").Value, Owner, null, Now).Value;
    }
}
