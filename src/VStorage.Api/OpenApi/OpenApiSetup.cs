using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using VStorage.Api.Auth;
using VStorage.Api.Configuration;

namespace VStorage.Api.OpenApi
{
    internal static class OpenApiSetup
    {
        public static IServiceCollection AddVStorageOpenApi(this IServiceCollection services) =>
            services.AddOpenApi(options => options
                .AddDocumentTransformer((document, _, _) =>
                {
                    document.Info.Title = "VStorage API";
                    document.Info.Version = "v1";
                    document.Info.Description = "Internal file storage: buckets, objects, download and share links.";
                    return Task.CompletedTask;
                })
                .AddDocumentTransformer<KeycloakSecuritySchemeTransformer>());

        /// <summary>
        /// /openapi/v1.json and the Scalar UI (/scalar/v1) when VStorage:Docs:Enabled.
        /// "Authorize" in Scalar signs in through Keycloak (public client vstorage-scalar, PKCE).
        /// </summary>
        public static WebApplication MapVStorageDocs(this WebApplication app)
        {
            if (!app.Services.GetRequiredService<IOptions<DocsOptions>>().Value.Enabled)
            {
                return app;
            }

            var auth = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference(options => options
                    .WithTitle("VStorage")
                    .AddPreferredSecuritySchemes([KeycloakSecuritySchemeTransformer.KeycloakScheme])
                    .AddAuthorizationCodeFlow(KeycloakSecuritySchemeTransformer.KeycloakScheme, flow =>
                    {
                        flow.ClientId = auth.ScalarClientId;
                        flow.Pkce = Pkce.Sha256;
                        flow.SelectedScopes = ["openid", "profile"];
                    }))
                .AllowAnonymous();

            return app;
        }
    }
}
