using System.Text;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Buckets;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;
using VStorage.Domain.Objects;

namespace VStorage.Application.Features.Objects
{
    /// <summary>
    /// FR-O11 listing: objects visible to the caller (shared + own private) in key byte order, then id; with a delimiter,
    /// keys below the next delimiter after the prefix roll up into common prefixes. Limit counts objects and prefixes together.
    /// Non-members (platform admins included) get 404.
    /// </summary>
    public sealed class ObjectListingService(IVStorageDbContext db, IObjectListingQuery query)
    {
        public const int DefaultLimit = 100;
        public const int MaxLimit = 1000;

        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        public async Task<Result<ObjectListing>> ListAsync(
            CurrentPrincipal caller, Guid bucketId, ListObjectsRequest request, CancellationToken cancellationToken)
        {
            var limit = request.Limit ?? DefaultLimit;
            if (limit is < 1 or > MaxLimit)
            {
                return ListingErrors.InvalidLimit;
            }

            var prefix = request.Prefix ?? "";
            if (!IsValidPrefix(prefix))
            {
                return ListingErrors.InvalidPrefix;
            }

            var delimiter = request.Delimiter ?? "";
            if (delimiter.Length > 0 && delimiter is not [>= '!' and <= '~'])
            {
                return ListingErrors.InvalidDelimiter;
            }

            var (seekKey, seekId) = ("", Guid.Empty);
            if (!string.IsNullOrEmpty(request.Cursor))
            {
                var cursor = ListingCursor.Decode(request.Cursor);
                if (cursor is null || cursor.Prefix != prefix || cursor.Delimiter != delimiter)
                {
                    return ListingErrors.InvalidCursor;
                }

                (seekKey, seekId) = (cursor.SeekKey, cursor.SeekId);
            }

            var bucket = await db.FindBucketAsync(bucketId, cancellationToken);
            if (!BucketPermissions.CanRead(bucket?.RoleOf(caller.Id)))
            {
                return BucketErrors.NotFound;
            }

            // One extra row tells whether another page exists.
            var rows = await query.ListAsync(new ListingQuery(bucketId, caller.Id, prefix, delimiter, seekKey, seekId, limit + 1), cancellationToken);
            var page = rows.Take(limit).ToList();
            var nextCursor = rows.Count > limit
                ? new ListingCursor(page[^1].SeekKey, page[^1].SeekId, prefix, delimiter).Encode()
                : null;

            return new ObjectListing(
                page.Where(row => row.Object is not null).Select(row => row.Object!).ToList(),
                page.Where(row => row.CommonPrefix is not null).Select(row => row.CommonPrefix!).ToList(),
                nextCursor);
        }

        // Same character rules as a key, but may be empty and may end with or contain "/" anywhere.
        private static bool IsValidPrefix(string prefix)
        {
            try
            {
                return StrictUtf8.GetByteCount(prefix) <= ObjectKey.MaxBytes && !prefix.Any(c => char.IsControl(c) || c == '\\');
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
