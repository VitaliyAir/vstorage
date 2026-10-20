using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using VStorage.Api.Auth;
using Testcontainers.PostgreSql;

namespace VStorage.Api.IntegrationTests
{
    /// <summary>
    /// Runs the API against a throw-away PostgreSQL 18 container (requires Docker) and a temp blob root.
    /// Environment "Testing": no user-secrets; migrations are applied on startup.
    /// Tokens are signed by <see cref="TestTokens"/> and go through the REAL JwtBearer validation
    /// (only the Keycloak metadata/keys are replaced).
    /// </summary>
    public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        /// <summary>Upload limit in tests (1 MiB) so "too large" can be tested cheaply.</summary>
        public const long MaxUploadBytes = 1024 * 1024;

        /// <summary>Where the /s sign-in challenge redirects in tests (stands in for Keycloak's authorization endpoint).</summary>
        public const string KeycloakAuthorizeUrl = "https://keycloak.test/realms/test/protocol/openid-connect/auth";

        private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18").Build();

        public string BlobRoot { get; } = Path.Combine(Path.GetTempPath(), "vstorage-tests", Guid.NewGuid().ToString("N"));

        public async ValueTask InitializeAsync() => await _database.StartAsync();

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _database.DisposeAsync();
            if (Directory.Exists(BlobRoot))
            {
                Directory.Delete(BlobRoot, recursive: true);
            }
        }

        /// <summary>A client that sends <paramref name="token"/> as bearer token.</summary>
        public HttpClient CreateClient(string token)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:VStorage", _database.GetConnectionString());
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.UseSetting("VStorage:PublicBaseUrl", "https://files.test");
            builder.UseSetting("VStorage:Storage:BlobRoot", BlobRoot);
            builder.UseSetting("VStorage:DataProtection:KeysPath", Path.Combine(BlobRoot, "keys"));
            builder.UseSetting("VStorage:Storage:MaxUploadBytes", MaxUploadBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("VStorage:Auth:Authority", TestTokens.Issuer);
            builder.UseSetting("VStorage:Auth:RequireHttpsMetadata", "false");
            builder.UseSetting("VStorage:Docs:Enabled", "true");

            builder.ConfigureTestServices(services => services.PostConfigure<OpenIdConnectOptions>(
                ShareSignIn.OidcScheme,
                oidc =>
                {
                    // /s sign-in: no Keycloak discovery; the challenge redirects to this fake authorization endpoint.
                    var configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer, AuthorizationEndpoint = KeycloakAuthorizeUrl };
                    oidc.Configuration = configuration;
                    oidc.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                }));

            builder.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                jwt =>
                {
                    // Same validation rules as production; issuer metadata and signing key come from the test issuer.
                    var configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
                    configuration.SigningKeys.Add(TestTokens.SigningKey);
                    jwt.Authority = null;
                    jwt.Configuration = configuration;
                    jwt.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    jwt.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
                    jwt.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
                }));
        }
    }

    [CollectionDefinition(Name)]
    public sealed class ApiCollection : ICollectionFixture<ApiFactory>
    {
        public const string Name = "api";
    }
}
