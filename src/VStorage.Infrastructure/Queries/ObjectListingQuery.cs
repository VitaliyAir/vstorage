using Dapper;
using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.Objects;
using VStorage.Domain.Objects;
using VStorage.Infrastructure.Persistence;

namespace VStorage.Infrastructure.Queries
{
    /// <summary>
    /// Listing as an index skip-scan in one round trip: a recursive CTE takes the next visible entry after the current seek
    /// position (ix_objects_listing: bucket_id, key COLLATE "C", id). An object moves the position to its (key, id); a common
    /// prefix "docs/" jumps to "docs0" ('/' + 1), past every key under it, so a page never reads a folder's contents.
    /// </summary>
    internal sealed class ObjectListingQuery(VStorageDbContext db) : IObjectListingQuery
    {
        // The next visible candidate rows from a position; LIKE with a literal prefix becomes an index range under COLLATE "C".
        private const string Candidates = """
            SELECT o.id, p.prefix,
                   CASE WHEN p.prefix IS NULL THEN o.key ELSE left(p.prefix, -1) || @DelimiterNext END COLLATE "C" AS seek_key,
                   CASE WHEN p.prefix IS NULL THEN o.id ELSE '00000000-0000-0000-0000-000000000000'::uuid END AS seek_id
            FROM objects o
            CROSS JOIN LATERAL (
                SELECT CASE WHEN @Delimiter <> '' AND strpos(substr(o.key, char_length(@Prefix) + 1), @Delimiter) > 0
                            THEN left(o.key, char_length(@Prefix) + strpos(substr(o.key, char_length(@Prefix) + 1), @Delimiter))
                       END COLLATE "C" AS prefix
            ) p
            WHERE o.bucket_id = @BucketId
              AND o.status = 'available'
              AND (o.visibility = 'shared' OR o.owner_id = @CallerId)
              AND o.key LIKE @Pattern
            """;

        private const string Sql = $"""
            WITH RECURSIVE walk AS (
                (
                    SELECT 1 AS n, c.*
                    FROM ({Candidates}
                          AND (o.key, o.id) > (@SeekKey COLLATE "C", @SeekId)
                          ORDER BY o.key, o.id
                          LIMIT 1) c
                )
                UNION ALL
                (
                    SELECT w.n + 1, c.*
                    FROM walk w
                    CROSS JOIN LATERAL ({Candidates}
                          AND (o.key, o.id) > (w.seek_key, w.seek_id)
                          ORDER BY o.key, o.id
                          LIMIT 1) c
                    WHERE w.n < @Take
                )
            )
            SELECT w.prefix AS CommonPrefix, w.seek_key AS SeekKey, w.seek_id AS SeekId,
                   o.id AS Id, o.key AS Key, o.visibility AS Visibility, o.owner_id AS OwnerId, o.size AS Size, o.sha256 AS Sha256,
                   o.content_type AS ContentType, o.original_file_name AS OriginalFileName, o.created_at AS CreatedAt
            FROM walk w
            LEFT JOIN objects o ON w.prefix IS NULL AND o.id = w.id
            ORDER BY w.n
            """;

        public async Task<IReadOnlyList<ListingRow>> ListAsync(ListingQuery query, CancellationToken cancellationToken)
        {
            var parameters = new
            {
                query.BucketId,
                query.CallerId,
                query.Prefix,
                Pattern = LikePrefix(query.Prefix),
                query.Delimiter,
                DelimiterNext = query.Delimiter.Length == 1 ? ((char)(query.Delimiter[0] + 1)).ToString() : "",
                query.SeekKey,
                query.SeekId,
                query.Take
            };

            // The DbContext's connection: Dapper opens and closes it when EF Core has not.
            var rows = await db.Database.GetDbConnection()
                .QueryAsync<Row>(new CommandDefinition(Sql, parameters, cancellationToken: cancellationToken));

            return rows.Select(row => row.ToListingRow(query.BucketId)).ToList();
        }

        // "100%_" must match literally: escape LIKE wildcards (backslash is the default LIKE escape character).
        private static string LikePrefix(string prefix) =>
            prefix.Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("%", @"\%", StringComparison.Ordinal)
                .Replace("_", @"\_", StringComparison.Ordinal) + "%";

        private sealed class Row
        {
            public string? CommonPrefix { get; set; }

            public string SeekKey { get; set; } = "";

            public Guid SeekId { get; set; }

            public Guid? Id { get; set; }

            public string? Key { get; set; }

            public string? Visibility { get; set; }

            public Guid? OwnerId { get; set; }

            public long? Size { get; set; }

            public string? Sha256 { get; set; }

            public string? ContentType { get; set; }

            public string? OriginalFileName { get; set; }

            // Npgsql reads timestamptz as a UTC DateTime.
            public DateTime? CreatedAt { get; set; }

            public ListingRow ToListingRow(Guid bucketId)
            {
                if (CommonPrefix is not null)
                {
                    return new ListingRow(null, CommonPrefix, SeekKey, SeekId);
                }

                var storedObject = new ObjectDto(
                    Id!.Value,
                    bucketId,
                    Key!,
                    Enum.Parse<Visibility>(Visibility!, ignoreCase: true),
                    OwnerId!.Value,
                    Size ?? 0,
                    Sha256 ?? "",
                    ContentType!,
                    OriginalFileName,
                    new DateTimeOffset(DateTime.SpecifyKind(CreatedAt!.Value, DateTimeKind.Utc)));
                return new ListingRow(storedObject, null, SeekKey, SeekId);
            }
        }
    }
}
