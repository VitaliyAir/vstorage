using Microsoft.AspNetCore.Authentication;
using Microsoft.Net.Http.Headers;
using VStorage.Api.Auth;
using VStorage.Api.Http;
using VStorage.Application.Features.ShareLinks;
using VStorage.Domain.Sharing;

namespace VStorage.Api.Endpoints
{
    /// <summary>Share links (spec §5.8): management on /api/v1, redemption on /s/{slug} in the browser.</summary>
    internal static class ShareLinkEndpoints
    {
        private static readonly string[] GetAndHead = [HttpMethods.Get, HttpMethods.Head];

        public static RouteGroupBuilder MapShareLinkEndpoints(this RouteGroupBuilder api)
        {
            var onObject = api.MapGroup("/objects/{objectId:guid}/share-links").WithTags("Share links");
            onObject.MapPost("", Create)
                .WithSummary("Create a share link: recipientUserIds? (empty = public), expiresAt?, maxDownloads?")
                .Produces<ShareLinkDto>(StatusCodes.Status201Created);
            onObject.MapGet("", ListForObject)
                .WithSummary("Links of the object: your own; all of them for a shared object if you are bucket owner/admin")
                .Produces<ShareLinkDto[]>();

            var links = api.MapGroup("/share-links").WithTags("Share links");
            links.MapGet("/received", ListReceived)
                .WithSummary("Active addressed links where you are a recipient (shared with me)")
                .Produces<ReceivedShareLinkDto[]>();
            links.MapDelete("/{shareLinkId:guid}", Revoke)
                .WithSummary("Revoke (creator, or bucket owner/admin for a shared object)");

            return api;
        }

        /// <summary>/s/{slug}: no prefix; public links need nothing, addressed links a session from the Keycloak sign-in.</summary>
        public static IEndpointRouteBuilder MapSharePages(this IEndpointRouteBuilder app)
        {
            app.MapMethods("/s/{slug}", GetAndHead, Redeem)
                .AllowAnonymous()
                .WithTags("Share links")
                .WithSummary("Open a share link (?disposition=inline for safe types); addressed links redirect to the Keycloak sign-in");

            return app;
        }

        private static async Task<IResult> Create(
            Guid objectId, CreateShareLinkRequest request, HttpContext http, ShareLinkService links, CancellationToken cancellationToken)
        {
            var created = await links.CreateAsync(http.RequireCurrentPrincipal(), objectId, request, cancellationToken);
            return created.IsSuccess
                ? TypedResults.Created($"/api/v1/objects/{objectId}/share-links", created.Value)
                : created.Error.ToProblem();
        }

        private static async Task<IResult> ListForObject(Guid objectId, HttpContext http, ShareLinkService links, CancellationToken cancellationToken) =>
            (await links.ListForObjectAsync(http.RequireCurrentPrincipal(), objectId, cancellationToken)).ToHttp();

        private static async Task<IResult> ListReceived(HttpContext http, ShareLinkService links, CancellationToken cancellationToken) =>
            TypedResults.Ok(await links.ListReceivedAsync(http.RequireCurrentPrincipal(), cancellationToken));

        private static async Task<IResult> Revoke(Guid shareLinkId, HttpContext http, ShareLinkService links, CancellationToken cancellationToken) =>
            (await links.RevokeAsync(http.RequireCurrentPrincipal(), shareLinkId, cancellationToken)).ToHttp();

        private static async Task<IResult> Redeem(
            string slug, string? disposition, HttpContext http, ShareLinkRedeemService redeem, CancellationToken cancellationToken)
        {
            // SEC-7 on every answer of /s, errors and redirects included.
            http.Response.Headers["X-Robots-Tag"] = "noindex";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";

            var session = await http.AuthenticateAsync(ShareSignIn.CookieScheme);
            var subject = session.Succeeded ? session.Principal?.FindFirst(ShareSignIn.SubjectClaim)?.Value : null;

            var result = await redeem.RedeemAsync(slug, subject, CountsAsDownload(http.Request), cancellationToken);
            if (result.IsSuccess)
            {
                return result.Value.ToFileResult(http, disposition);
            }

            if (result.Error == ShareLinkErrors.SignInRequired)
            {
                // FR-A10: Keycloak sign-in, then back to the same /s/{slug} (query string kept).
                var returnUrl = $"{http.Request.PathBase}{http.Request.Path}{http.Request.QueryString}";
                return TypedResults.Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, [ShareSignIn.OidcScheme]);
            }

            return result.Error.ToProblem();
        }

        // FR-S5: only a GET that starts at byte 0 counts; HEAD and Range continuations do not.
        private static bool CountsAsDownload(HttpRequest request)
        {
            if (!HttpMethods.IsGet(request.Method))
            {
                return false;
            }

            var range = request.Headers.Range.ToString();
            if (range.Length == 0 || !RangeHeaderValue.TryParse(range, out var parsed))
            {
                // No or unusable Range header: the whole file is served.
                return true;
            }

            return parsed.Ranges.FirstOrDefault()?.From == 0;
        }
    }
}
