namespace VStorage.Domain.Buckets
{
    /// <summary>Role of a principal in a bucket, ordered by power. Owner comes from buckets.owner_id, the others from bucket_members.</summary>
    public enum BucketRole
    {
        Viewer = 1,
        Member = 2,
        Admin = 3,
        Owner = 4
    }
}
