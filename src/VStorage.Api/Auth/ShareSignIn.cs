using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace VStorage.Api.Auth
{
    /// <summary>
    /// Browser sign-in for addressed share links only (FR-A10, FR-A11, SEC-10). /api/v1 keeps using JWT bearer; these two
    /// schemes are used explicitly by /s/{slug} and never become the default.
    /// </summary>
    internal static class ShareSignIn
    {
        /// <summary>Encrypted session cookie: HttpOnly, Secure, SameSite=Lax, path /s, 1 h, only "sub" inside.</summary>
        public const string CookieScheme = "share";

        /// <summary>Authorization code + PKCE against Keycloak with the confidential client VStorage:Auth:Oidc:ClientId.</summary>
        public const string OidcScheme = "share-oidc";

        public const string CookieName = "vstorage.share";
        public const string SubjectClaim = "sub";

        public static IServiceCollection AddShareSignIn(this IServiceCollection services)
        {
            services.AddAuthentication()
                .AddCookie(CookieScheme, cookie =>
                {
                    cookie.Cookie.Name = CookieName;
                    cookie.Cookie.Path = "/s";
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    cookie.Cookie.SameSite = SameSiteMode.Lax;
                    cookie.ExpireTimeSpan = TimeSpan.FromHours(1);
                    cookie.SlidingExpiration = false;
                })
                .AddOpenIdConnect(OidcScheme, _ => { });

            services.AddOptions<OpenIdConnectOptions>(OidcScheme)
                .Configure<IOptions<AuthOptions>>((oidc, authOptions) =>
                {
                    var auth = authOptions.Value;
                    oidc.Authority = auth.Authority;
                    oidc.RequireHttpsMetadata = auth.RequireHttpsMetadata;

                    // Docker: discovery, keys and the code exchange go through the internal Keycloak address; the browser is
                    // still sent to the public one (Keycloak builds its URLs from the public Host header).
                    if (KeycloakBackchannelHandler.Create(auth.Authority, auth.InternalAuthority) is { } backchannel)
                    {
                        oidc.BackchannelHttpHandler = backchannel;
                    }
                    oidc.ClientId = auth.Oidc.ClientId;
                    oidc.ClientSecret = auth.Oidc.ClientSecret;
                    oidc.ResponseType = "code";
                    oidc.UsePkce = true;
                    oidc.SignInScheme = CookieScheme;
                    oidc.CallbackPath = "/signin-oidc";
                    oidc.SignedOutCallbackPath = "/signout-callback-oidc";
                    oidc.MapInboundClaims = false;
                    oidc.GetClaimsFromUserInfoEndpoint = false;

                    // No Keycloak tokens in the cookie (FR-A11): they are dropped after sign-in.
                    oidc.SaveTokens = false;
                    oidc.Scope.Clear();
                    oidc.Scope.Add("openid");

                    oidc.Events = new OpenIdConnectEvents
                    {
                        // Keep only "sub": the cookie must not carry profile data or roles.
                        OnTokenValidated = context =>
                        {
                            var subject = context.Principal?.FindFirst(SubjectClaim)?.Value;
                            if (string.IsNullOrWhiteSpace(subject))
                            {
                                context.Fail("The ID token has no 'sub' claim.");
                                return Task.CompletedTask;
                            }

                            context.Principal = new ClaimsPrincipal(
                                new ClaimsIdentity([new Claim(SubjectClaim, subject)], CookieScheme, SubjectClaim, null));
                            return Task.CompletedTask;
                        }
                    };
                });

            return services;
        }
    }
}
