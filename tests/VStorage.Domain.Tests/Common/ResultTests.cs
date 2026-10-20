using VStorage.Domain.Common;

namespace VStorage.Domain.Tests.Common
{
    public sealed class ResultTests
    {
        private static readonly Error SomeError = Error.NotFound("not-found", "Nothing here.");

        [Fact]
        public void Success_HasNoError()
        {
            var result = Result.Success();

            result.IsSuccess.Should().BeTrue();
            result.IsFailure.Should().BeFalse();
            FluentActions.Invoking(() => result.Error).Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void Failure_CarriesError()
        {
            Result result = SomeError;

            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be(SomeError);
        }

        [Fact]
        public void GenericSuccess_CarriesValue()
        {
            Result<int> result = 42;

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(42);
        }

        [Fact]
        public void GenericFailure_HasNoValue()
        {
            Result<int> result = SomeError;

            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(ErrorType.NotFound);
            FluentActions.Invoking(() => result.Value).Should().Throw<InvalidOperationException>();
        }
    }
}
