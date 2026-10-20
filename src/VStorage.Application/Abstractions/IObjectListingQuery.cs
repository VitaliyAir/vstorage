using VStorage.Application.Features.Objects;

namespace VStorage.Application.Abstractions
{
    /// <summary>
    /// One page of a bucket listing (FR-O11), implemented in SQL: delimiter roll-up into common prefixes needs a skip-scan
    /// that EF Core cannot express.
    /// </summary>
    public interface IObjectListingQuery
    {
        Task<IReadOnlyList<ListingRow>> ListAsync(ListingQuery query, CancellationToken cancellationToken);
    }

    /// <param name="Prefix">Literal key prefix ("" = all keys).</param>
    /// <param name="Delimiter">One ASCII character, or "" for a flat listing.</param>
    /// <param name="SeekKey">Entries strictly after (<paramref name="SeekKey"/>, <paramref name="SeekId"/>) are returned; ("", empty) = from the start.</param>
    /// <param name="Take">Maximum number of entries (objects + common prefixes).</param>
    public sealed record ListingQuery(Guid BucketId, Guid CallerId, string Prefix, string Delimiter, string SeekKey, Guid SeekId, int Take);

    /// <summary>
    /// Either an object or a common prefix, in key byte order. (<see cref="SeekKey"/>, <see cref="SeekId"/>) is the position
    /// after this entry: the object's (key, id), or for a prefix the first key past every key under it.
    /// </summary>
    public sealed record ListingRow(ObjectDto? Object, string? CommonPrefix, string SeekKey, Guid SeekId);
}
