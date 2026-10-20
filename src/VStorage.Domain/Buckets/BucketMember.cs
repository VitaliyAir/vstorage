namespace VStorage.Domain.Buckets
{
    /// <summary>A non-owner role in a bucket (table bucket_members). Part of the <see cref="Bucket"/> aggregate.</summary>
    public sealed class BucketMember
    {
        private BucketMember()
        {
        }

        internal BucketMember(Guid bucketId, Guid userId, BucketRole role, Guid addedBy, DateTimeOffset joinedAt)
        {
            BucketId = bucketId;
            UserId = userId;
            Role = role;
            AddedBy = addedBy;
            JoinedAt = joinedAt;
        }

        public Guid BucketId { get; private set; }

        public Guid UserId { get; private set; }

        public BucketRole Role { get; private set; }

        public Guid AddedBy { get; private set; }

        public DateTimeOffset JoinedAt { get; private set; }

        internal void ChangeRole(BucketRole role) => Role = role;
    }
}
