namespace VStorage.Api.Auth
{
    /// <summary>
    /// Authorization policy names. These are the ONLY places where Keycloak roles are used;
    /// access to a bucket always comes from the database (owner / bucket_members).
    /// </summary>
    internal static class AuthPolicies
    {
        /// <summary>Client role vstorage-admin: /api/v1/admin/*.</summary>
        public const string PlatformAdmin = "PlatformAdmin";

        /// <summary>Client role bucket-creator (or vstorage-admin): POST /api/v1/buckets.</summary>
        public const string BucketCreator = "BucketCreator";
    }
}
