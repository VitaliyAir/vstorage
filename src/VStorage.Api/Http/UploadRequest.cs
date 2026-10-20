using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;
using VStorage.Application.Features.Objects;
using VStorage.Domain.Common;

namespace VStorage.Api.Http
{
    /// <summary>HTTP details of uploads: body size limit, preconditions, file name, small multipart fields.</summary>
    internal static class UploadRequest
    {
        private static readonly Error UnsupportedPrecondition = Error.Validation(
            "unsupported-precondition", "Use 'If-None-Match: *', 'If-Match: *' or 'If-Match: \"<sha256>\"' (one of them).");

        /// <summary>Raises Kestrel's per-request body limit (default 30 MB) to the upload limit for this request only.</summary>
        public static void AllowBodySize(HttpContext http, long maxBytes)
        {
            var feature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = maxBytes;
            }
        }

        /// <summary>FR-O6 preconditions from If-None-Match / If-Match.</summary>
        public static Result<UploadPrecondition> ParsePrecondition(IHeaderDictionary headers)
        {
            var ifNoneMatch = headers.IfNoneMatch.ToString().Trim();
            var ifMatch = headers.IfMatch.ToString().Trim();

            if (ifNoneMatch.Length > 0 && ifMatch.Length > 0)
            {
                return UnsupportedPrecondition;
            }

            if (ifNoneMatch.Length > 0)
            {
                return ifNoneMatch == "*" ? new UploadPrecondition(UploadPreconditionKind.IfNoneMatchAny) : UnsupportedPrecondition;
            }

            if (ifMatch.Length == 0)
            {
                return UploadPrecondition.None;
            }

            if (ifMatch == "*")
            {
                return new UploadPrecondition(UploadPreconditionKind.IfMatchAny);
            }

            if (EntityTagHeaderValue.TryParse(ifMatch, out var tag) && !tag.IsWeak)
            {
                var sha = HeaderUtilities.RemoveQuotes(tag.Tag).Value!.ToLowerInvariant();
                return new UploadPrecondition(UploadPreconditionKind.IfMatchETag, sha);
            }

            return UnsupportedPrecondition;
        }

        /// <summary>Original file name from a Content-Disposition value (filename* preferred).</summary>
        public static string? FileName(string? contentDisposition)
        {
            if (!ContentDispositionHeaderValue.TryParse(contentDisposition, out var disposition))
            {
                return null;
            }

            return FileName(disposition);
        }

        public static string? FileName(ContentDispositionHeaderValue disposition)
        {
            var name = disposition.FileNameStar.HasValue ? disposition.FileNameStar.Value : HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        /// <summary>Reads a small multipart text field; null when it is longer than <paramref name="maxChars"/>.</summary>
        public static async Task<string?> ReadFieldAsync(Stream body, int maxChars, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            var buffer = new char[maxChars + 1];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = await reader.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
            {
                total += read;
            }

            return total > maxChars ? null : new string(buffer, 0, total);
        }
    }
}
