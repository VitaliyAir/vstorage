using VStorage.Domain.Common;

namespace VStorage.Domain.Buckets
{
    public static class BucketErrors
    {
        public static readonly Error InvalidName = Error.Validation(
            "invalid-bucket-name", "Bucket name must be 3-63 characters: lower-case letters, digits and hyphens, not starting or ending with a hyphen.");

        public static readonly Error InvalidQuota = Error.Validation("invalid-quota", "Quota must be empty or greater than zero.");

        public static readonly Error NotFound = Error.NotFound("not-found", "Bucket not found.");

        public static readonly Error NameTaken = Error.Conflict("bucket-name-taken", "You already own a bucket with this name.");

        public static readonly Error InvalidMemberRole = Error.Validation("invalid-role", "Member role must be admin, member or viewer.");

        public static readonly Error AlreadyMember = Error.Conflict("already-member", "The user already has a role in this bucket.");

        public static readonly Error MemberNotFound = Error.NotFound("member-not-found", "The user is not a member of this bucket.");

        public static readonly Error OwnerCannotBeChanged = Error.Conflict(
            "owner-cannot-be-changed", "The owner cannot be removed or given another role; transfer the ownership instead.");

        public static readonly Error OwnerCannotLeave = Error.Conflict("owner-cannot-leave", "The owner cannot leave the bucket; transfer the ownership first.");

        public static readonly Error AlreadyOwner = Error.Conflict("already-owner", "The user already owns this bucket.");

        public static readonly Error NewOwnerNotMember = Error.Conflict("new-owner-not-member", "The new owner must be a member of the bucket.");

        public static readonly Error QuotaExceeded = Error.TooLarge("quota-exceeded", "The bucket quota would be exceeded.");
    }
}
