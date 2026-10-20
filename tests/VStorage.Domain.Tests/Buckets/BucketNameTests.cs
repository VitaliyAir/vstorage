using VStorage.Domain.Buckets;

namespace VStorage.Domain.Tests.Buckets
{
    public sealed class BucketNameTests
    {
        [Theory]
        [InlineData("abc")]
        [InlineData("vgplanner-attachments")]
        [InlineData("a1-b2-c3")]
        [InlineData("123")]
        public void Valid_names_are_accepted(string value)
        {
            BucketName.Create(value).Value.Value.Should().Be(value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("ab")]
        [InlineData("-abc")]
        [InlineData("abc-")]
        [InlineData("Abc")]
        [InlineData("a_b_c")]
        [InlineData("a.b.c")]
        public void Invalid_names_are_rejected(string? value)
        {
            BucketName.Create(value).Error.Should().Be(BucketErrors.InvalidName);
        }

        [Fact]
        public void Length_is_limited_to_63()
        {
            BucketName.Create(new string('a', 63)).IsSuccess.Should().BeTrue();
            BucketName.Create(new string('a', 64)).IsFailure.Should().BeTrue();
        }
    }
}
