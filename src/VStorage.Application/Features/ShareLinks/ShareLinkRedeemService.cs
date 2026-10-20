using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Objects;
using VStorage.Domain.Common;
using VStorage.Domain.Sharing;

namespace VStorage.Application.Features.ShareLinks
{
    /// <summary>
    /// GET/HEAD /s/{slug} (FR-S5): unknown or revoked link, object gone, or signed-in user not a recipient → 404;
    /// expired → 410; addressed link without session → sign-in. A counted download (GET starting at byte 0) increases the
    /// counter atomically and fails with 410 "exhausted" at maxDownloads; Range continuations are not counted and still
    /// served, so an interrupted download can be resumed.
    /// </summary>
    public sealed class ShareLinkRedeemService(IVStorageDbContext db, ObjectReadService objects, IClock clock)
    {
        /// <param name="sessionSub">Keycloak "sub" from the /s session cookie, or null.</param>
        /// <param name="countsAsDownload">True for a GET without Range or with a range starting at byte 0.</param>
        public async Task<Result<ObjectContent>> RedeemAsync(
            string slug, string? sessionSub, bool countsAsDownload, CancellationToken cancellationToken)
        {
            if (!ShareLink.IsValidSlug(slug))
            {
                return ShareLinkErrors.NotFound;
            }

            var link = await db.ShareLinks.Include(l => l.Recipients).SingleOrDefaultAsync(l => l.Slug == slug, cancellationToken);
            if (link is null)
            {
                return ShareLinkErrors.NotFound;
            }

            var state = link.StateAt(clock.UtcNow);
            if (state == ShareLinkState.Revoked)
            {
                return ShareLinkErrors.NotFound;
            }

            if (state == ShareLinkState.Expired)
            {
                return ShareLinkErrors.Expired;
            }

            Guid? principalId = null;
            if (link.Kind == ShareLinkKind.Addressed)
            {
                if (sessionSub is null)
                {
                    return ShareLinkErrors.SignInRequired;
                }

                principalId = await db.Principals
                    .Where(p => p.KeycloakSub == sessionSub)
                    .Select(p => (Guid?)p.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                if (principalId is null || !link.IsRecipient(principalId.Value))
                {
                    return ShareLinkErrors.NotFound;
                }
            }

            if (state == ShareLinkState.Exhausted && countsAsDownload)
            {
                return ShareLinkErrors.Exhausted;
            }

            var opened = await objects.OpenAvailableAsync(link.ObjectId, cancellationToken);
            if (opened.IsFailure || !countsAsDownload)
            {
                return opened.IsSuccess ? opened.Value : ShareLinkErrors.NotFound;
            }

            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var counted = await db.ShareLinks
                    .Where(l => l.Id == link.Id && l.RevokedAt == null && (l.MaxDownloads == null || l.Downloads < l.MaxDownloads))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.Downloads, l => l.Downloads + 1), cancellationToken);
                if (counted == 0)
                {
                    await opened.Value.Content.DisposeAsync();
                    return ShareLinkErrors.Exhausted;
                }

                link.RecordRedemption(principalId);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return opened.Value;
            }
            catch
            {
                await opened.Value.Content.DisposeAsync();
                throw;
            }
        }
    }
}
