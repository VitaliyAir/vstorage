using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;
using VStorage.Domain.Buckets;
using VStorage.Domain.Principals;

namespace VStorage.Application.Features.Admin
{
    public sealed record AdminUserDto(
        Guid Id, PrincipalKind Kind, string? DisplayName, string? Email, bool EmailVerified, string KeycloakSub, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);

    public sealed record AdminBucketDto(
        Guid Id, string Name, Guid OwnerId, string? OwnerDisplayName, long? QuotaBytes, long UsedBytes, int MemberCount, DateTimeOffset CreatedAt);

    public sealed record AdminCreateBucketRequest(string? Name, Guid OwnerId, long? QuotaBytes);

    /// <summary>
    /// Read side of the Admin API (/api/v1/admin, client role vstorage-admin): find principals (users and service
    /// accounts) and list all buckets. Admin changes go through BucketService / MemberService with BucketAccess.AsAdmin.
    /// </summary>
    public sealed class AdminService(IVStorageDbContext db)
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 200;

        /// <summary>Matches part of the email or display name, or the exact Keycloak sub.</summary>
        public async Task<AdminUserDto[]> SearchUsersAsync(string? search, int? limit, CancellationToken cancellationToken)
        {
            var query = db.Principals.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var exact = search.Trim();
                var term = exact.ToLowerInvariant();
                query = query.Where(p =>
                    p.KeycloakSub == exact
                    || (p.Email != null && p.Email.Contains(term))
                    || (p.DisplayName != null && p.DisplayName.ToLower().Contains(term)));
            }

            return await query
                .OrderBy(p => p.DisplayName)
                .ThenBy(p => p.Id)
                .Take(Clamp(limit))
                .Select(p => new AdminUserDto(p.Id, p.Kind, p.DisplayName, p.Email, p.EmailVerified, p.KeycloakSub, p.CreatedAt, p.LastSeenAt))
                .ToArrayAsync(cancellationToken);
        }

        /// <summary>All live buckets, optionally by exact name and/or owner.</summary>
        public async Task<AdminBucketDto[]> ListBucketsAsync(string? name, Guid? ownerId, int? limit, CancellationToken cancellationToken)
        {
            var query = db.Buckets.AsNoTracking().Include(b => b.Members).Where(b => b.DeletedAt == null);
            if (ownerId is Guid owner)
            {
                query = query.Where(b => b.OwnerId == owner);
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var bucketName = BucketName.Create(name.Trim());
                if (bucketName.IsFailure)
                {
                    return [];
                }

                query = query.Where(b => b.Name == bucketName.Value);
            }

            var buckets = await query.OrderBy(b => b.Name).ThenBy(b => b.Id).Take(Clamp(limit)).ToListAsync(cancellationToken);

            var ownerIds = buckets.Select(b => b.OwnerId).Distinct().ToList();
            var ownerNames = await db.Principals.AsNoTracking()
                .Where(p => ownerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);

            return buckets
                .Select(b => new AdminBucketDto(
                    b.Id, b.Name.Value, b.OwnerId, ownerNames.GetValueOrDefault(b.OwnerId), b.QuotaBytes, b.UsedBytes, b.Members.Count, b.CreatedAt))
                .ToArray();
        }

        private static int Clamp(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
    }
}
