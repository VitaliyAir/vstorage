using Microsoft.Net.Http.Headers;
using VStorage.Application.Features.Objects;
using VStorage.Domain.Objects;

namespace VStorage.Api.Http
{
    /// <summary>
    /// Turns an opened object into a streamed file response: Range, ETag (= SHA-256), Last-Modified, conditional
    /// requests (304/412) and HEAD are handled by the framework file result. Adds the stored-XSS headers (SEC-6).
    /// Reused by /d/{token} and /s/{slug} in later stages.
    /// </summary>
    internal static class ObjectDownload
    {
        public static IResult ToFileResult(this ObjectContent content, HttpContext http, string? requestedDisposition)
        {
            var storedObject = content.Object;
            var inline = string.Equals(requestedDisposition, "inline", StringComparison.OrdinalIgnoreCase)
                && ContentDispositionPolicy.IsInlineAllowed(storedObject.ContentType);

            var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
            disposition.SetHttpFileName(storedObject.OriginalFileName ?? LastSegment(storedObject.Key));

            var headers = http.Response.Headers;
            headers.ContentDisposition = disposition.ToString();
            headers.XContentTypeOptions = "nosniff";
            headers.ContentSecurityPolicy = "sandbox";

            return TypedResults.Stream(
                content.Content,
                storedObject.ContentType,
                lastModified: storedObject.CreatedAt,
                entityTag: new EntityTagHeaderValue($"\"{storedObject.Sha256}\""),
                enableRangeProcessing: true);
        }

        private static string LastSegment(string key) => key[(key.LastIndexOf('/') + 1)..];
    }
}
