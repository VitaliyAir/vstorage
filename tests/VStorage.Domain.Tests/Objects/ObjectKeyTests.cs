using VStorage.Domain.Objects;

namespace VStorage.Domain.Tests.Objects
{
    public sealed class ObjectKeyTests
    {
        [Theory]
        [InlineData("cat.jpg")]
        [InlineData("photos/2026/10/cat.jpg")]
        [InlineData("0193a1b2/tasks/42/0193a1b3")]
        [InlineData("файлы/отчёт.pdf")]
        [InlineData("a..b/c.d")]
        public void Valid_keys_are_accepted(string value)
        {
            ObjectKey.Create(value).Value.Value.Should().Be(value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("/leading")]
        [InlineData("trailing/")]
        [InlineData("double//slash")]
        [InlineData("../etc/passwd")]
        [InlineData("a/./b")]
        [InlineData("a/../b")]
        [InlineData("back\\slash")]
        [InlineData("tab\there")]
        [InlineData("new\nline")]
        public void Invalid_keys_are_rejected(string? value)
        {
            ObjectKey.Create(value).Error.Code.Should().Be("invalid-key");
        }

        [Fact]
        public void Length_is_counted_in_utf8_bytes()
        {
            ObjectKey.Create(new string('a', 1024)).IsSuccess.Should().BeTrue();
            ObjectKey.Create(new string('a', 1025)).IsFailure.Should().BeTrue();

            // "ж" is 2 bytes in UTF-8: 512 chars = 1024 bytes, 513 chars = 1026 bytes.
            ObjectKey.Create(new string('ж', 512)).IsSuccess.Should().BeTrue();
            ObjectKey.Create(new string('ж', 513)).IsFailure.Should().BeTrue();
        }

        [Fact]
        public void Lone_surrogate_is_rejected()
        {
            ObjectKey.Create("bad\uD800key").IsFailure.Should().BeTrue();
        }
    }
}
