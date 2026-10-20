using System.Security.Claims;
using VStorage.Application.Features.Principals;
using VStorage.Domain.Principals;

namespace VStorage.Api.Auth
{
    /// <summary>Reads the Keycloak claims VStorage uses (MapInboundClaims is off: JWT claim names as is).</summary>
    internal static class TokenClaims
    {
        /// <summary>Keycloak names the service-account user of client X "service-account-x".</summary>
        public const string ServiceAccountPrefix = "service-account-";

        /// <summary>Identity for provisioning, or null when the token has no "sub".</summary>
        public static PrincipalClaims? ToPrincipalClaims(ClaimsPrincipal user)
        {
            var subject = user.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                return null;
            }

            var userName = user.FindFirst("preferred_username")?.Value;
            var kind = userName?.StartsWith(ServiceAccountPrefix, StringComparison.Ordinal) == true
                ? PrincipalKind.Service
                : PrincipalKind.User;
            var emailVerified = bool.TryParse(user.FindFirst("email_verified")?.Value, out var verified) && verified;
            var displayName = user.FindFirst("name")?.Value ?? userName;

            return new PrincipalClaims(subject, kind, user.FindFirst("email")?.Value, emailVerified, displayName);
        }

        /// <summary>The calling client: "azp" (set by Keycloak, also after token exchange), else "client_id".</summary>
        public static string? ClientId(ClaimsPrincipal user) =>
            user.FindFirst("azp")?.Value ?? user.FindFirst("client_id")?.Value;

        /// <summary>RFC 8693 delegation chain, if the token has one (raw JSON).</summary>
        public static string? Actor(ClaimsPrincipal user) => user.FindFirst("act")?.Value;
    }
}
