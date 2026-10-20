using VStorage.Application.Features.Objects;
using VStorage.Domain.Common;

namespace VStorage.Application.Features.DownloadLinks
{
    /// <summary>Content of a download token: which object, for whose access, how to serve it, until when (FR-D3).</summary>
    public sealed record DownloadToken(Guid ObjectId, Guid PrincipalId, bool Inline, DateTimeOffset ExpiresAt);

    /// <param name="ExpiresInSeconds">Lifetime; null = VStorage:DownloadLinks:DefaultLifetimeSeconds.</param>
    /// <param name="Disposition">"inline" or "attachment" (default). Inline is only honoured for safe types (SEC-6).</param>
    public sealed record CreateDownloadLinkRequest(int? ExpiresInSeconds, string? Disposition);

    public sealed record DownloadLinkDto(string Url, DateTimeOffset ExpiresAt);

    /// <summary>An opened object plus the disposition from the token.</summary>
    public sealed record DownloadLinkContent(ObjectContent Content, string Disposition);

    public static class DownloadLinkErrors
    {
        public static readonly Error InvalidDisposition = Error.Validation(
            "invalid-disposition", "Disposition must be 'inline' or 'attachment'.");

        /// <summary>FR-D4: an authentic token after its expiry.</summary>
        public static readonly Error Expired = Error.Gone("expired", "The download link has expired.");

        /// <summary>FR-D4 / SEC-5: unknown or tampered token - the same answer as a missing object.</summary>
        public static readonly Error NotFound = Error.NotFound("not-found", "Object not found.");

        public static Error InvalidLifetime(int maxSeconds) => Error.Validation(
            "invalid-lifetime", $"expiresInSeconds must be between 1 and {maxSeconds}.");
    }
}
