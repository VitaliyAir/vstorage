using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;
using VStorage.Domain.Common;
using VStorage.Domain.Principals;

namespace VStorage.Application.Features.Principals
{
    /// <summary>Lazy provisioning of principals from tokens, "me" and exact user lookup.</summary>
    public sealed class PrincipalService(IVStorageDbContext db, IClock clock)
    {
        private const int MaxEmailLength = 320;
        private static readonly TimeSpan LastSeenInterval = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Returns the principal for the token's "sub", creating it on first use (FR-A3) and refreshing
        /// email / display name when they changed (FR-A4). Writes only when something changed.
        /// </summary>
        public async Task<Principal> ProvisionAsync(PrincipalClaims claims, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;
            var principal = await db.Principals.SingleOrDefaultAsync(p => p.KeycloakSub == claims.Subject, cancellationToken);

            if (principal is null)
            {
                principal = Principal.Create(claims.Subject, claims.Kind, claims.Email, claims.EmailVerified, claims.DisplayName, now);
                db.Principals.Add(principal);
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    return principal;
                }
                catch (DbUpdateException)
                {
                    // A parallel first request created the same principal (unique keycloak_sub): use that row.
                    db.Principals.Remove(principal);
                    var existing = await db.Principals.SingleOrDefaultAsync(p => p.KeycloakSub == claims.Subject, cancellationToken);
                    if (existing is null)
                    {
                        throw;
                    }

                    principal = existing;
                }
            }

            var changed = principal.UpdateProfile(claims.Email, claims.EmailVerified, claims.DisplayName);
            changed |= principal.Touch(now, LastSeenInterval);
            if (changed)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return principal;
        }

        public async Task<Result<PrincipalDto>> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            var principal = await db.Principals.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new PrincipalDto(p.Id, p.Kind, p.DisplayName, p.Email, p.EmailVerified))
                .SingleOrDefaultAsync(cancellationToken);

            if (principal is null)
            {
                return PrincipalErrors.NotFound;
            }

            return principal;
        }

        /// <summary>Exact match on a verified email (FR-U2): no search, no partial match.</summary>
        public async Task<Result<UserLookupDto>> LookupByEmailAsync(string? email, CancellationToken cancellationToken)
        {
            var normalized = email?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxEmailLength || !IsCompleteEmail(normalized))
            {
                return PrincipalErrors.InvalidEmail;
            }

            var user = await db.Principals.AsNoTracking()
                .Where(p => p.EmailVerified && p.Email == normalized)
                .OrderBy(p => p.CreatedAt)
                .Select(p => new UserLookupDto(p.Id, p.DisplayName))
                .FirstOrDefaultAsync(cancellationToken);

            if (user is null)
            {
                return PrincipalErrors.UserNotFound;
            }

            return user;
        }

        private static bool IsCompleteEmail(string value)
        {
            var at = value.IndexOf('@');
            return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1;
        }
    }
}
