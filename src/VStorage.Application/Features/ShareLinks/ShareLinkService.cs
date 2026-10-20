using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Buckets;
using VStorage.Application.Features.Objects;
using VStorage.Application.Options;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;
using VStorage.Domain.Objects;
using VStorage.Domain.Sharing;

namespace VStorage.Application.Features.ShareLinks
{
    /// <summary>
    /// Create, list, received and revoke share links (FR-S1..S4, S6..S8). Rights (spec §3): own object - member or above;
    /// another user's SHARED object - owner/admin; another user's private object is invisible (404).
    /// </summary>
    public sealed class ShareLinkService(
        IVStorageDbContext db, ISlugGenerator slugs, IClock clock, IOptions<ShareLinkOptions> linkOptions, IOptions<VStorageOptions> options)
    {
        public const int MaxRecipients = 100;
        public const int MaxReceived = 500;
        private const int SlugAttempts = 3;

        public async Task<Result<ShareLinkDto>> CreateAsync(
            CurrentPrincipal caller, Guid objectId, CreateShareLinkRequest request, CancellationToken cancellationToken)
        {
            var loaded = await LoadObjectAsync(caller.Id, objectId, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded.Error;
            }

            var (storedObject, role) = loaded.Value;
            var allowed = storedObject.OwnerId == caller.Id
                ? BucketPermissions.CanUpload(role)
                : BucketPermissions.CanManageOthersSharedObjects(role);
            if (!allowed)
            {
                return ObjectAccessErrors.Forbidden;
            }

            var recipients = (request.RecipientUserIds ?? []).Distinct().ToList();
            if (recipients.Count > MaxRecipients)
            {
                return ShareLinkErrors.TooManyRecipients(MaxRecipients);
            }

            if (recipients.Count > 0 && await db.Principals.CountAsync(p => recipients.Contains(p.Id), cancellationToken) != recipients.Count)
            {
                return ShareLinkErrors.UnknownRecipient;
            }

            var settings = linkOptions.Value;
            var now = clock.UtcNow;
            var expiresAt = request.ExpiresAt?.ToUniversalTime() ?? now + settings.DefaultExpiresIn;
            if (expiresAt > now + settings.MaxExpiresIn)
            {
                return ShareLinkErrors.ExpiryTooLate(now + settings.MaxExpiresIn);
            }

            var slugLength = recipients.Count == 0 ? settings.PublicSlugLength : settings.AddressedSlugLength;
            for (var attempt = 1; ; attempt++)
            {
                var created = ShareLink.Create(objectId, slugs.Create(slugLength), caller.Id, recipients, expiresAt, request.MaxDownloads, now);
                if (created.IsFailure)
                {
                    return created.Error;
                }

                var link = created.Value;
                db.ShareLinks.Add(link);
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    return ToDto(link, now);
                }
                catch (DbUpdateException) when (attempt < SlugAttempts)
                {
                    // Slug collision (unique index on slug); practically never with 47+ random bits.
                    db.ChangeTracker.Clear();
                }
            }
        }

        /// <summary>FR-S6: the caller's own links; for a shared object the bucket owner/admins see all of them.</summary>
        public async Task<Result<ShareLinkDto[]>> ListForObjectAsync(CurrentPrincipal caller, Guid objectId, CancellationToken cancellationToken)
        {
            var loaded = await LoadObjectAsync(caller.Id, objectId, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded.Error;
            }

            var (storedObject, role) = loaded.Value;
            var seesAll = storedObject.Visibility == Visibility.Shared && BucketPermissions.CanManageOthersSharedObjects(role);
            var links = await db.ShareLinks.AsNoTracking()
                .Include(l => l.Recipients)
                .Where(l => l.ObjectId == objectId && (seesAll || l.CreatedBy == caller.Id))
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync(cancellationToken);

            var now = clock.UtcNow;
            return links.Select(link => ToDto(link, now)).ToArray();
        }

        /// <summary>FR-S7: active addressed links for the caller whose object is still available (newest first, at most 500).</summary>
        public async Task<ReceivedShareLinkDto[]> ListReceivedAsync(CurrentPrincipal caller, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;
            var rows = await db.ShareLinks.AsNoTracking()
                .Where(l => l.Kind == ShareLinkKind.Addressed
                    && l.RevokedAt == null
                    && l.ExpiresAt > now
                    && (l.MaxDownloads == null || l.Downloads < l.MaxDownloads)
                    && l.Recipients.Any(r => r.UserId == caller.Id))
                .Join(
                    db.Objects.AsNoTracking().Where(o => o.Status == ObjectStatus.Available),
                    link => link.ObjectId,
                    storedObject => storedObject.Id,
                    (link, storedObject) => new { Link = link, Object = storedObject })
                .OrderByDescending(row => row.Link.CreatedAt)
                .Take(MaxReceived)
                .ToListAsync(cancellationToken);

            return rows.Select(row => new ReceivedShareLinkDto(
                    row.Link.Id,
                    UrlOf(row.Link.Slug),
                    row.Object.OriginalFileName ?? LastSegment(row.Object.Key.Value),
                    row.Object.ContentType,
                    row.Object.Size ?? 0,
                    row.Link.ExpiresAt,
                    row.Link.CreatedBy,
                    row.Link.CreatedAt))
                .ToArray();
        }

        /// <summary>FR-S8: the creator, or a bucket owner/admin for a shared object. Anyone else gets 404.</summary>
        public async Task<Result> RevokeAsync(CurrentPrincipal caller, Guid shareLinkId, CancellationToken cancellationToken)
        {
            var link = await db.ShareLinks.SingleOrDefaultAsync(l => l.Id == shareLinkId, cancellationToken);
            if (link is null)
            {
                return ShareLinkErrors.NotFound;
            }

            if (link.CreatedBy != caller.Id)
            {
                var loaded = await LoadObjectAsync(caller.Id, link.ObjectId, cancellationToken);
                if (loaded.IsFailure
                    || loaded.Value.Object.Visibility != Visibility.Shared
                    || !BucketPermissions.CanManageOthersSharedObjects(loaded.Value.Role))
                {
                    return ShareLinkErrors.NotFound;
                }
            }

            var revoked = link.Revoke(clock.UtcNow);
            if (revoked.IsFailure)
            {
                return revoked;
            }

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        // Available object the caller may see (shared, or own private) plus the caller's bucket role; otherwise 404.
        private async Task<Result<(StoredObject Object, BucketRole Role)>> LoadObjectAsync(
            Guid callerId, Guid objectId, CancellationToken cancellationToken)
        {
            var storedObject = await db.Objects.AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == objectId && o.Status == ObjectStatus.Available, cancellationToken);
            if (storedObject is null || !storedObject.IsVisibleTo(callerId))
            {
                return ObjectErrors.NotFound;
            }

            var bucket = await db.FindBucketAsync(storedObject.BucketId, cancellationToken);
            if (bucket?.RoleOf(callerId) is not BucketRole role)
            {
                return ObjectErrors.NotFound;
            }

            return (storedObject, role);
        }

        private ShareLinkDto ToDto(ShareLink link, DateTimeOffset now) => new(
            link.Id,
            link.ObjectId,
            UrlOf(link.Slug),
            link.Kind,
            link.Recipients.Select(r => r.UserId).ToList(),
            link.ExpiresAt,
            link.MaxDownloads,
            link.Downloads,
            link.StateAt(now),
            link.CreatedBy,
            link.CreatedAt,
            link.RevokedAt);

        private string UrlOf(string slug) => $"{options.Value.PublicBaseUrl.TrimEnd('/')}/s/{slug}";

        private static string LastSegment(string key) => key[(key.LastIndexOf('/') + 1)..];
    }
}
