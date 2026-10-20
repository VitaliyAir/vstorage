using Microsoft.Extensions.Options;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Objects;
using VStorage.Application.Options;
using VStorage.Domain.Common;

namespace VStorage.Application.Features.DownloadLinks
{
    /// <summary>
    /// Short-lived download links for Consumer UIs (spec §5.7). Stateless: everything is inside the encrypted token, so a
    /// link cannot be revoked (FR-D5). At download time the creator's access is checked again (FR-D4).
    /// </summary>
    public sealed class DownloadLinkService(
        ObjectReadService objects,
        IDownloadTokenProtector protector,
        IClock clock,
        IOptions<DownloadLinkOptions> linkOptions,
        IOptions<VStorageOptions> options)
    {
        /// <summary>FR-D1/D2: any principal who can read the object.</summary>
        public async Task<Result<DownloadLinkDto>> CreateAsync(
            CurrentPrincipal caller, Guid objectId, CreateDownloadLinkRequest request, CancellationToken cancellationToken)
        {
            var settings = linkOptions.Value;
            var lifetime = request.ExpiresInSeconds ?? settings.DefaultLifetimeSeconds;
            if (lifetime < 1 || lifetime > settings.MaxLifetimeSeconds)
            {
                return DownloadLinkErrors.InvalidLifetime(settings.MaxLifetimeSeconds);
            }

            var inline = ParseDisposition(request.Disposition);
            if (inline is null)
            {
                return DownloadLinkErrors.InvalidDisposition;
            }

            var visible = await objects.GetMetadataAsync(caller, objectId, cancellationToken);
            if (visible.IsFailure)
            {
                return visible.Error;
            }

            // Whole seconds: the token stores Unix seconds.
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(clock.UtcNow.ToUnixTimeSeconds() + lifetime);
            var token = protector.Protect(new DownloadToken(objectId, caller.Id, inline.Value, expiresAt));
            return new DownloadLinkDto($"{options.Value.PublicBaseUrl.TrimEnd('/')}/d/{token}", expiresAt);
        }

        /// <summary>FR-D4: unknown/tampered → 404, expired → 410, object gone or creator without access → 404.</summary>
        public async Task<Result<DownloadLinkContent>> OpenAsync(string token, CancellationToken cancellationToken)
        {
            var payload = protector.Unprotect(token);
            if (payload is null)
            {
                return DownloadLinkErrors.NotFound;
            }

            if (clock.UtcNow >= payload.ExpiresAt)
            {
                return DownloadLinkErrors.Expired;
            }

            var content = await objects.OpenByIdForAsync(payload.PrincipalId, payload.ObjectId, cancellationToken);
            if (content.IsFailure)
            {
                return content.Error;
            }

            return new DownloadLinkContent(content.Value, payload.Inline ? "inline" : "attachment");
        }

        // null = invalid; true = inline; false = attachment (also when empty).
        private static bool? ParseDisposition(string? value) =>
            value?.Trim().ToLowerInvariant() switch
            {
                null or "" or "attachment" => false,
                "inline" => true,
                _ => null
            };
    }
}
