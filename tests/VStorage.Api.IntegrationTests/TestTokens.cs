using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace VStorage.Api.IntegrationTests
{
    /// <summary>Test stand-in for Keycloak: signs access tokens shaped like Keycloak's with a local RSA key.</summary>
    public static class TestTokens
    {
        public const string Issuer = "http://localhost/realms/test";

        public static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-key" };

        /// <param name="audiences">"aud"; one value is written as a string, several as an array (both occur in Keycloak).</param>
        /// <param name="clientRoles">Roles per client, written to resource_access.{client}.roles.</param>
        public static string Create(
            string subject,
            string[]? audiences = null,
            string? azp = "vgplanner-api",
            string? preferredUserName = null,
            string? email = null,
            bool emailVerified = false,
            string? name = null,
            IReadOnlyDictionary<string, string[]>? clientRoles = null,
            DateTime? expires = null)
        {
            var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(5);
            var aud = audiences ?? ["vstorage"];

            var claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["aud"] = aud.Length == 1 ? aud[0] : aud,
                ["email_verified"] = emailVerified
            };
            AddIfSet(claims, "azp", azp);
            AddIfSet(claims, "preferred_username", preferredUserName);
            AddIfSet(claims, "email", email);
            AddIfSet(claims, "name", name);
            if (clientRoles is not null)
            {
                claims["resource_access"] = clientRoles.ToDictionary(
                    pair => pair.Key,
                    pair => (object)new Dictionary<string, object> { ["roles"] = pair.Value });
            }

            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Claims = claims,
                IssuedAt = expiresAt.AddMinutes(-10),
                NotBefore = expiresAt.AddMinutes(-10),
                Expires = expiresAt,
                SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256)
            };

            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        /// <summary>A Keycloak service-account token (client credentials) of <paramref name="clientId"/>.</summary>
        public static string ServiceAccount(string clientId, string subject, IReadOnlyDictionary<string, string[]>? clientRoles = null) =>
            Create(subject, azp: clientId, preferredUserName: "service-account-" + clientId, clientRoles: clientRoles);

        private static void AddIfSet(Dictionary<string, object> claims, string name, string? value)
        {
            if (value is not null)
            {
                claims[name] = value;
            }
        }
    }
}
