using System.Buffers.Text;
using System.Text;
using VStorage.Application.Features.Objects;

namespace VStorage.Application.Tests.Objects
{
    public sealed class ListingCursorTests
    {
        [Fact]
        public void Encode_then_decode_round_trips()
        {
            var cursor = new ListingCursor("docs/Привет.txt", Guid.NewGuid(), "docs/", "/");

            var encoded = cursor.Encode();

            encoded.Should().MatchRegex("^[A-Za-z0-9_-]+$");
            ListingCursor.Decode(encoded).Should().Be(cursor);
        }

        [Theory]
        [InlineData("not base64 !")]
        [InlineData("e30")] // {}
        [InlineData("bnVsbA")] // null
        [InlineData("eyJrIjoxfQ")] // {"k":1}
        public void Malformed_values_decode_to_null(string value) =>
            ListingCursor.Decode(value).Should().BeNull();

        [Fact]
        public void Missing_fields_decode_to_null() =>
            ListingCursor.Decode(Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"k":"a","i":"00000000-0000-0000-0000-000000000000"}""")))
                .Should().BeNull();

        [Fact]
        public void Too_long_values_decode_to_null() =>
            ListingCursor.Decode(new string('A', ListingCursor.MaxEncodedLength + 1)).Should().BeNull();
    }
}
