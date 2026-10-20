using VStorage.Domain.Common;

namespace VStorage.Application.Features.Objects
{
    public sealed record ListObjectsRequest(string? Prefix, string? Delimiter, string? Cursor, int? Limit);

    /// <param name="NextCursor">Pass it as ?cursor= (with the same prefix and delimiter) for the next page; null on the last page.</param>
    public sealed record ObjectListing(IReadOnlyList<ObjectDto> Objects, IReadOnlyList<string> CommonPrefixes, string? NextCursor);

    public static class ListingErrors
    {
        public static readonly Error InvalidLimit = Error.Validation(
            "invalid-limit", $"Limit must be between 1 and {ObjectListingService.MaxLimit}.");

        public static readonly Error InvalidPrefix = Error.Validation(
            "invalid-prefix", "Prefix must be valid UTF-8, at most 1024 bytes, without control characters or backslashes.");

        public static readonly Error InvalidDelimiter = Error.Validation(
            "invalid-delimiter", "Delimiter must be a single printable ASCII character, e.g. '/'.");

        public static readonly Error InvalidCursor = Error.Validation(
            "invalid-cursor", "The cursor is malformed or belongs to a listing with another prefix or delimiter.");
    }
}
