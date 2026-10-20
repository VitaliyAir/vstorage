using System.Text.RegularExpressions;
using VStorage.Domain.Common;
using VStorage.Domain.Events;

namespace VStorage.Domain.Sharing
{
    public enum ShareLinkKind
    {
        Public,
        Addressed
    }

    public enum ShareLinkState
    {
        Active,
        Revoked,
        Expired,
        Exhausted
    }

    /// <summary>Recipient of an addressed share link (table share_link_recipients).</summary>
    public sealed class ShareLinkRecipient
    {
        private ShareLinkRecipient()
        {
        }

        internal ShareLinkRecipient(Guid shareLinkId, Guid userId)
        {
            ShareLinkId = shareLinkId;
            UserId = userId;
        }

        public Guid ShareLinkId { get; private set; }

        public Guid UserId { get; private set; }
    }

    /// <summary>
    /// Short link /s/{slug} to one object: public (no recipients) or addressed (listed recipients, sign-in required).
    /// The download counter is increased atomically in SQL when the link is redeemed (see <see cref="RecordRedemption"/>).
    /// </summary>
    public sealed partial class ShareLink : Entity
    {
        private readonly List<ShareLinkRecipient> _recipients = [];

        private ShareLink()
        {
        }

        private ShareLink(Guid id, Guid objectId, string slug, ShareLinkKind kind, Guid createdBy, DateTimeOffset expiresAt, int? maxDownloads, DateTimeOffset now)
            : base(id)
        {
            ObjectId = objectId;
            Slug = slug;
            Kind = kind;
            CreatedBy = createdBy;
            ExpiresAt = expiresAt;
            MaxDownloads = maxDownloads;
            CreatedAt = now;
        }

        public Guid ObjectId { get; private set; }

        public string Slug { get; private set; } = "";

        public ShareLinkKind Kind { get; private set; }

        public Guid CreatedBy { get; private set; }

        public DateTimeOffset ExpiresAt { get; private set; }

        public int? MaxDownloads { get; private set; }

        public int Downloads { get; private set; }

        public DateTimeOffset? RevokedAt { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public IReadOnlyCollection<ShareLinkRecipient> Recipients => _recipients;

        /// <summary>Slugs are base62, 8–32 characters.</summary>
        public static bool IsValidSlug(string? slug) => slug is not null && SlugPattern().IsMatch(slug);

        public static Result<ShareLink> Create(
            Guid objectId, string slug, Guid createdBy, IReadOnlyCollection<Guid> recipientIds, DateTimeOffset expiresAt, int? maxDownloads, DateTimeOffset now)
        {
            if (!IsValidSlug(slug))
            {
                return ShareLinkErrors.InvalidSlug;
            }

            if (expiresAt <= now)
            {
                return ShareLinkErrors.ExpiryInPast;
            }

            if (maxDownloads is <= 0)
            {
                return ShareLinkErrors.InvalidMaxDownloads;
            }

            var recipients = recipientIds.Distinct().ToList();
            var kind = recipients.Count == 0 ? ShareLinkKind.Public : ShareLinkKind.Addressed;
            var link = new ShareLink(Guid.CreateVersion7(now), objectId, slug, kind, createdBy, expiresAt, maxDownloads, now);
            link._recipients.AddRange(recipients.Select(userId => new ShareLinkRecipient(link.Id, userId)));
            link.Raise(new ShareLinkCreated(link.Id, objectId, kind, createdBy));
            return link;
        }

        public Result Revoke(DateTimeOffset now)
        {
            if (RevokedAt is not null)
            {
                return ShareLinkErrors.AlreadyRevoked;
            }

            RevokedAt = now;
            Raise(new ShareLinkRevoked(Id, ObjectId));
            return Result.Success();
        }

        /// <summary>
        /// Records a counted download (FR-S5, FR-L5). The counter itself is increased atomically in SQL by the caller;
        /// this only raises <see cref="ShareLinkRedeemed"/>. <paramref name="principalId"/> is null for public links.
        /// </summary>
        public void RecordRedemption(Guid? principalId) => Raise(new ShareLinkRedeemed(Id, ObjectId, principalId));

        public ShareLinkState StateAt(DateTimeOffset now)
        {
            if (RevokedAt is not null)
            {
                return ShareLinkState.Revoked;
            }

            if (now >= ExpiresAt)
            {
                return ShareLinkState.Expired;
            }

            return MaxDownloads is int max && Downloads >= max ? ShareLinkState.Exhausted : ShareLinkState.Active;
        }

        public bool IsRecipient(Guid userId) => _recipients.Any(r => r.UserId == userId);

        [GeneratedRegex("^[0-9A-Za-z]{8,32}$")]
        private static partial Regex SlugPattern();
    }
}
