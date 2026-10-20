namespace VStorage.Domain.Buckets
{
    /// <summary>
    /// Permission matrix of spec §3 for the bucket API. A null role = no role in the bucket (callers answer 404).
    /// Another user's PRIVATE object is never accessible, whatever the role (checked on the object, not here).
    /// </summary>
    public static class BucketPermissions
    {
        /// <summary>See the bucket, its members, list and read shared objects.</summary>
        public static bool CanRead(BucketRole? role) => role is not null;

        /// <summary>Upload (shared or private), manage own objects, create share links for own objects.</summary>
        public static bool CanUpload(BucketRole? role) => role >= BucketRole.Member;

        /// <summary>Overwrite/delete another user's shared object, share it, see/revoke links on shared objects.</summary>
        public static bool CanManageOthersSharedObjects(BucketRole? role) => role >= BucketRole.Admin;

        /// <summary>Add, remove or set a member with <paramref name="targetRole"/>. Owner: any role; admin: members and viewers only.</summary>
        public static bool CanManageMember(BucketRole? actorRole, BucketRole targetRole) =>
            targetRole != BucketRole.Owner
            && (actorRole == BucketRole.Owner || (actorRole == BucketRole.Admin && targetRole <= BucketRole.Member));

        /// <summary>Change quota, delete the bucket, transfer ownership.</summary>
        public static bool CanManageBucket(BucketRole? role) => role == BucketRole.Owner;

        public static bool CanLeave(BucketRole? role) => role is not null && role != BucketRole.Owner;
    }
}
