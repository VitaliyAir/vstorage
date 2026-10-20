using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VStorage.Application.Features.Objects
{
    /// <summary>
    /// Opaque continuation token: base64url JSON of the seek position plus the prefix and delimiter it belongs to.
    /// Not signed: a crafted cursor can only move the start position inside what the caller may see anyway.
    /// </summary>
    public sealed record ListingCursor(
        [property: JsonPropertyName("k")] string SeekKey,
        [property: JsonPropertyName("i")] Guid SeekId,
        [property: JsonPropertyName("p")] string Prefix,
        [property: JsonPropertyName("d")] string Delimiter)
    {
        public const int MaxEncodedLength = 4096;

        public string Encode() => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(this));

        /// <summary>Null when <paramref name="value"/> is not a cursor produced by <see cref="Encode"/>.</summary>
        public static ListingCursor? Decode(string value)
        {
            if (value.Length > MaxEncodedLength)
            {
                return null;
            }

            try
            {
                var cursor = JsonSerializer.Deserialize<ListingCursor>(Base64Url.DecodeFromChars(value));
                return cursor is { SeekKey: not null, Prefix: not null, Delimiter: not null } ? cursor : null;
            }
            catch (Exception exception) when (exception is FormatException or JsonException)
            {
                return null;
            }
        }
    }
}
