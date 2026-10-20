using VStorage.Domain.Objects;

namespace VStorage.Domain.Tests.Objects
{
    public sealed class ContentDispositionPolicyTests
    {
        [Theory]
        [InlineData("image/png", true)]
        [InlineData("image/jpeg", true)]
        [InlineData("IMAGE/PNG", true)]
        [InlineData("application/pdf", true)]
        [InlineData("video/mp4", true)]
        [InlineData("audio/mpeg", true)]
        [InlineData("text/plain; charset=utf-8", true)]
        [InlineData("image/svg+xml", false)]
        [InlineData("text/html", false)]
        [InlineData("application/xhtml+xml", false)]
        [InlineData("text/xml", false)]
        [InlineData("application/javascript", false)]
        [InlineData("application/octet-stream", false)]
        public void Only_passive_types_are_shown_inline(string contentType, bool inline)
        {
            ContentDispositionPolicy.IsInlineAllowed(contentType).Should().Be(inline);
        }
    }
}
