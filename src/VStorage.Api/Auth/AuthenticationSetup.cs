using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VStorage.Application.Abstractions;

namespace VStorage.Api.Auth
{
    internal static class AuthenticationSetup
    {
        /// <summary>
        /// JWT bearer for /api/v1: Keycloak realm (VStorage:Auth:Authority), "aud" must contain VStorage:Auth:Audience,
        /// signature / issuer / lifetime validated. Client roles of VStorage:Auth:RolesClientId become role claims.
        /// </summary>
        public static IServiceCollection AddVStorageAuthentication(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IEventContext, HttpEventContext>();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<AuthOptions>>((jwt, authOptions) =>
                {
                    var auth = authOptions.Value;
                    jwt.Authority = auth.Authority;
                    jwt.RequireHttpsMetadata = auth.RequireHttpsMetadata;
                    jwt.MapInboundClaims = false;

                    // Docker: discovery and signing keys are fetched through the internal Keycloak address.
                    if (KeycloakBackchannelHandler.Create(auth.Authority, auth.InternalAuthority) is { } backchannel)
                    {
                        jwt.BackchannelHttpHandler = backchannel;
                    }

                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidAudience = auth.Audience,
                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        ValidateIssuerSigningKey = true,
                        ClockSkew = TimeSpan.FromSeconds(auth.ClockSkewSeconds),
                        NameClaimType = "preferred_username",
                        RoleClaimType = ClaimTypes.Role
                    };

                    jwt.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = context =>
                        {
                            if (context.Principal?.Identity is ClaimsIdentity identity)
                            {
                                MapClientRoles(identity, auth.RolesClientId);
                            }

                            return Task.CompletedTask;
                        }
                    };
                });

            return services;
        }

        /// <summary>
        /// Secure by default: every endpoint needs a valid token unless it says AllowAnonymous.
        /// Policies for the two platform roles (see <see cref="AuthPolicies"/>).
        /// </summary>
        public static IServiceCollection AddVStorageAuthorization(this IServiceCollection services)
        {
            services.AddAuthorization();
            services.AddOptions<AuthorizationOptions>()
                .Configure<IOptions<AuthOptions>>((authorization, authOptions) =>
                {
                    var auth = authOptions.Value;
                    authorization.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                    authorization.AddPolicy(AuthPolicies.PlatformAdmin, policy => policy
                        .RequireAuthenticatedUser()
                        .RequireRole(auth.AdminRole));
                    authorization.AddPolicy(AuthPolicies.BucketCreator, policy => policy
                        .RequireAuthenticatedUser()
                        .RequireRole(auth.BucketCreatorRole, auth.AdminRole));
                });

            return services;
        }

        // Client roles live in resource_access.<clientId>.roles, which ASP.NET does not read itself.
        // Only the roles of the vstorage client count: a role of another client grants nothing here.
        private static void MapClientRoles(ClaimsIdentity identity, string clientId)
        {
            var raw = identity.FindFirst("resource_access")?.Value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(clientId, out var client)
                || client.ValueKind != JsonValueKind.Object
                || !client.TryGetProperty("roles", out var roles)
                || roles.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var role in roles.EnumerateArray())
            {
                var value = role.ValueKind == JsonValueKind.String ? role.GetString() : null;
                if (!string.IsNullOrWhiteSpace(value) && !identity.HasClaim(ClaimTypes.Role, value))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, value));
                }
            }
        }
    }
}
