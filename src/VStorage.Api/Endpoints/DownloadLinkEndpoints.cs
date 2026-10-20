using VStorage.Api.Auth;
using VStorage.Api.Http;
using VStorage.Application.Features.DownloadLinks;

namespace VStorage.Api.Endpoints
{
    /// <summary>Short-lived download links (spec §5.7): created on /api/v1 by a Consumer API, opened by its browser UI.</summary>
    internal static class DownloadLinkEndpoints
    {
        private static readonly string[] GetAndHead = [HttpMethods.Get, HttpMethods.Head];

        public static RouteGroupBuilder MapDownloadLinkEndpoints(this RouteGroupBuilder api)
        {
            api.MapPost("/objects/{objectId:guid}/download-link", Create)
                .WithTags("Download links")
                .WithSummary("Create a short-lived /d/{token} link (anyone who can read the object): expiresInSeconds?, disposition?")
                .Produces<DownloadLinkDto>();

            return api;
        }

        /// <summary>/d/{token}: no prefix, no session; the encrypted token is the authorization (FR-D4, FR-A12).</summary>
        public static IEndpointRouteBuilder MapDownloadEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapMethods("/d/{token}", GetAndHead, Download)
                .AllowAnonymous()
                .WithTags("Download links")
                .WithSummary("Open a download link (Range supported); 404 unknown or no longer accessible, 410 expired");

            return app;
        }

        private static async Task<IResult> Create(
            Guid objectId, CreateDownloadLinkRequest request, HttpContext http, DownloadLinkService links, CancellationToken cancellationToken) =>
            (await links.CreateAsync(http.RequireCurrentPrincipal(), objectId, request, cancellationToken)).ToHttp();

        private static async Task<IResult> Download(string token, HttpContext http, DownloadLinkService links, CancellationToken cancellationToken)
        {
            // SEC-7 on every answer of /d, errors included: links must not be indexed or leak through the Referer header.
            http.Response.Headers["X-Robots-Tag"] = "noindex";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";

            var opened = await links.OpenAsync(token, cancellationToken);
            return opened.IsSuccess
                ? opened.Value.Content.ToFileResult(http, opened.Value.Disposition)
                : opened.Error.ToProblem();
        }
    }
}
