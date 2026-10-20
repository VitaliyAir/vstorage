using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;

namespace VStorage.Application.Features.Objects
{
    /// <summary>
    /// Atomic changes of buckets.used_bytes (FR-B8). A growth succeeds only while the quota allows it, so two parallel
    /// uploads can never exceed the quota together. Must run inside the transaction that changes the objects.
    /// </summary>
    internal static class BucketUsage
    {
        public static async Task<bool> TryChangeAsync(this IVStorageDbContext db, Guid bucketId, long delta, CancellationToken cancellationToken)
        {
            if (delta == 0)
            {
                return true;
            }

            var buckets = db.Buckets.Where(b => b.Id == bucketId);
            if (delta > 0)
            {
                buckets = buckets.Where(b => b.QuotaBytes == null || b.UsedBytes + delta <= b.QuotaBytes);
            }

            var updated = await buckets.ExecuteUpdateAsync(
                setters => setters.SetProperty(b => b.UsedBytes, b => b.UsedBytes + delta),
                cancellationToken);
            return updated == 1;
        }
    }
}
