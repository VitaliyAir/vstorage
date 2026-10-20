using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using VStorage.Api.Auth;

namespace VStorage.Api.OpenApi
{
    /// <summary>
    /// Two alternative security schemes in the OpenAPI document, so Scalar can call the protected API:
    /// "Keycloak" — sign in through Keycloak (authorization code + PKCE),
    /// "Bearer" — paste an access token (e.g. an exchanged consumer token) by hand.
    /// </summary>
    internal sealed class KeycloakSecuritySchemeTransformer(IOptions<AuthOptions> authOptions) : IOpenApiDocumentTransformer
    {
        public const string KeycloakScheme = "Keycloak";
        public const string BearerScheme = "Bearer";

        public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            var auth = authOptions.Value;
            var openIdConnect = auth.Authority.TrimEnd('/') + "/protocol/openid-connect";

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

            document.Components.SecuritySchemes[KeycloakScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = $"Sign in with Keycloak (client {auth.ScalarClientId}; token audience {auth.Audience}).",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri(openIdConnect + "/auth"),
                        TokenUrl = new Uri(openIdConnect + "/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            ["openid"] = "OpenID Connect sign-in",
                            ["profile"] = "User name"
                        }
                    }
                }
            };

            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = $"Keycloak access token whose \"aud\" contains {auth.Audience}."
            };

            // Separate requirements = either scheme is enough.
            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(KeycloakScheme, document)] = ["openid", "profile"]
            });
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = []
            });

            return Task.CompletedTask;
        }
    }
}
