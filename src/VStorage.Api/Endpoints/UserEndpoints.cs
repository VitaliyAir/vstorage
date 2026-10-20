using VStorage.Api.Auth;
using VStorage.Api.Http;
using VStorage.Application.Features.Principals;
using VStorage.Domain.Principals;

namespace VStorage.Api.Endpoints
{
    /// <summary>The caller's own record and exact user lookup (spec §5.3).</summary>
    internal static class UserEndpoints
    {
        public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
        {
            var group = api.MapGroup("").WithTags("Users");

            group.MapGet("/me", GetMe)
                .WithName("GetMe")
                .WithSummary("Current principal (created on first call)")
                .Produces<MeResponse>();

            group.MapGet("/users/lookup", Lookup)
                .WithName("LookupUser")
                .WithSummary("Exact lookup of an existing user by verified email")
                .Produces<UserLookupDto>()
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .RequireRateLimiting(RateLimiting.UserLookupPolicy);

            return api;
        }

        private static async Task<IResult> GetMe(HttpContext httpContext, PrincipalService principals, CancellationToken cancellationToken)
        {
            var current = httpContext.RequireCurrentPrincipal();
            var result = await principals.GetAsync(current.Id, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblem();
            }

            var principal = result.Value;
            return TypedResults.Ok(new MeResponse(
                principal.Id,
                principal.Kind,
                principal.DisplayName,
                principal.Email,
                principal.EmailVerified,
                current.ClientId,
                current.IsPlatformAdmin,
                current.CanCreateBuckets));
        }

        private static async Task<IResult> Lookup(string? email, PrincipalService principals, CancellationToken cancellationToken) =>
            (await principals.LookupByEmailAsync(email, cancellationToken)).ToHttp();
    }

    /// <param name="ClientId">Keycloak client the call came through ("azp").</param>
    internal sealed record MeResponse(
        Guid Id,
        PrincipalKind Kind,
        string? DisplayName,
        string? Email,
        bool EmailVerified,
        string? ClientId,
        bool IsPlatformAdmin,
        bool CanCreateBuckets);
}
