using Microsoft.Extensions.Options;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Principals;

namespace VStorage.Api.Auth
{
    /// <summary>
    /// After authentication: finds or creates the principal for the token's "sub" (lazy provisioning)
    /// and stores <see cref="CurrentPrincipal"/> for endpoints and the event context.
    /// </summary>
    internal sealed class PrincipalProvisioningMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context, PrincipalService principals, IOptions<AuthOptions> authOptions)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var claims = TokenClaims.ToPrincipalClaims(context.User);
                if (claims is null)
                {
                    await TypedResults.Problem(
                            statusCode: StatusCodes.Status401Unauthorized,
                            title: "Unauthorized",
                            detail: "The access token has no 'sub' claim.",
                            extensions: new Dictionary<string, object?> { ["code"] = "invalid-token" })
                        .ExecuteAsync(context);
                    return;
                }

                var principal = await principals.ProvisionAsync(claims, context.RequestAborted);
                var auth = authOptions.Value;
                var isPlatformAdmin = context.User.IsInRole(auth.AdminRole);

                context.SetCurrentPrincipal(new CurrentPrincipal(
                    principal.Id,
                    principal.Kind,
                    TokenClaims.ClientId(context.User),
                    isPlatformAdmin,
                    isPlatformAdmin || context.User.IsInRole(auth.BucketCreatorRole)));
            }

            await next(context);
        }
    }
}
