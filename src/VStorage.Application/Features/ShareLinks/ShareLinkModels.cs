using VStorage.Domain.Sharing;

namespace VStorage.Application.Features.ShareLinks
{
    /// <param name="RecipientUserIds">Empty or null = public link; otherwise addressed to these existing users (FR-S2).</param>
    /// <param name="ExpiresAt">UTC; null = now + VStorage:ShareLinks:DefaultExpiresIn (FR-S3).</param>
    /// <param name="MaxDownloads">null = unlimited.</param>
    public sealed record CreateShareLinkRequest(IReadOnlyList<Guid>? RecipientUserIds, DateTimeOffset? ExpiresAt, int? MaxDownloads);

    /// <summary>A share link as seen by its creator or a bucket owner/admin. The slug is part of <see cref="Url"/>.</summary>
    public sealed record ShareLinkDto(
        Guid Id,
        Guid ObjectId,
        string Url,
        ShareLinkKind Kind,
        IReadOnlyList<Guid> RecipientUserIds,
        DateTimeOffset ExpiresAt,
        int? MaxDownloads,
        int Downloads,
        ShareLinkState State,
        Guid CreatedBy,
        DateTimeOffset CreatedAt,
        DateTimeOffset? RevokedAt);

    /// <summary>
    /// "Shared with me" (FR-S7). Recipients need not be bucket members, so only what the link itself serves is shown:
    /// file name, type and size - no bucket, key or owner.
    /// </summary>
    public sealed record ReceivedShareLinkDto(
        Guid Id,
        string Url,
        string FileName,
        string ContentType,
        long Size,
        DateTimeOffset ExpiresAt,
        Guid CreatedBy,
        DateTimeOffset CreatedAt);
}
