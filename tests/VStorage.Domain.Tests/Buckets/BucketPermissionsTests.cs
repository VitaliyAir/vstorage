using VStorage.Domain.Buckets;

namespace VStorage.Domain.Tests.Buckets
{
    /// <summary>The permission matrix of spec §3, row by row.</summary>
    public sealed class BucketPermissionsTests
    {
        public static TheoryData<BucketRole?, bool, bool, bool, bool, bool> Matrix => new()
        {
            // role,               read,  upload, othersShared, manageBucket, leave
            { BucketRole.Owner,    true,  true,   true,         true,         false },
            { BucketRole.Admin,    true,  true,   true,         false,        true },
            { BucketRole.Member,   true,  true,   false,        false,        true },
            { BucketRole.Viewer,   true,  false,  false,        false,        true },
            { null,                false, false,  false,        false,        false }
        };

        [Theory]
        [MemberData(nameof(Matrix))]
        public void Role_rights_follow_the_matrix(BucketRole? role, bool read, bool upload, bool othersShared, bool manageBucket, bool leave)
        {
            BucketPermissions.CanRead(role).Should().Be(read);
            BucketPermissions.CanUpload(role).Should().Be(upload);
            BucketPermissions.CanManageOthersSharedObjects(role).Should().Be(othersShared);
            BucketPermissions.CanManageBucket(role).Should().Be(manageBucket);
            BucketPermissions.CanLeave(role).Should().Be(leave);
        }

        [Theory]
        [InlineData(BucketRole.Owner, BucketRole.Admin, true)]
        [InlineData(BucketRole.Owner, BucketRole.Member, true)]
        [InlineData(BucketRole.Owner, BucketRole.Viewer, true)]
        [InlineData(BucketRole.Owner, BucketRole.Owner, false)]
        [InlineData(BucketRole.Admin, BucketRole.Admin, false)]
        [InlineData(BucketRole.Admin, BucketRole.Member, true)]
        [InlineData(BucketRole.Admin, BucketRole.Viewer, true)]
        [InlineData(BucketRole.Member, BucketRole.Viewer, false)]
        [InlineData(BucketRole.Viewer, BucketRole.Viewer, false)]
        public void Member_management_follows_the_matrix(BucketRole actor, BucketRole target, bool allowed)
        {
            BucketPermissions.CanManageMember(actor, target).Should().Be(allowed);
        }

        [Fact]
        public void Without_role_no_member_can_be_managed()
        {
            BucketPermissions.CanManageMember(null, BucketRole.Viewer).Should().BeFalse();
        }
    }
}
