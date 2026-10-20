using System.ComponentModel.DataAnnotations;

namespace VStorage.Api.Auth
{
    /// <summary>Keycloak settings (section "VStorage:Auth"); used from stage S3 on.</summary>
    public sealed class AuthOptions
    {
        public const string SectionName = "VStorage:Auth";

        /// <summary>Realm URL, e.g. "http://localhost:8083/realms/VEco". Must equal the "iss" claim of tokens.</summary>
        [Required, Url]
        public string Authority { get; set; } = "";

        /// <summary>Value that the "aud" claim must contain.</summary>
        [Required]
        public string Audience { get; set; } = "vstorage";

        public bool RequireHttpsMetadata { get; set; } = true;

        /// <summary>
        /// Docker only: how this API reaches Keycloak when that differs from <see cref="Authority"/> as the browser sees it,
        /// e.g. "http://host.docker.internal:8083". Empty = use Authority directly (dev, tests).
        /// </summary>
        public string? InternalAuthority { get; set; }

        [Range(0, 300)]
        public int ClockSkewSeconds { get; set; } = 30;

        /// <summary>Keycloak client whose client roles are read (resource_access.{RolesClientId}.roles).</summary>
        [Required]
        public string RolesClientId { get; set; } = "vstorage";

        /// <summary>Client role for /api/v1/admin/*.</summary>
        [Required]
        public string AdminRole { get; set; } = "vstorage-admin";

        /// <summary>Client role required for POST /api/v1/buckets (the admin role is accepted too).</summary>
        [Required]
        public string BucketCreatorRole { get; set; } = "bucket-creator";

        /// <summary>Public Keycloak client used by the Scalar UI (authorization code + PKCE).</summary>
        [Required]
        public string ScalarClientId { get; set; } = "vstorage-scalar";

        /// <summary>Confidential client for the /s/{slug} sign-in (stage S8).</summary>
        public OidcClientOptions Oidc { get; set; } = new();
    }

    public sealed class OidcClientOptions
    {
        public string ClientId { get; set; } = "vstorage";

        /// <summary>Secret from Keycloak; user-secrets only.</summary>
        public string ClientSecret { get; set; } = "";
    }
}
