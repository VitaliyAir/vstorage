using VStorage.Domain.Principals;

namespace VStorage.Domain.Tests.Principals
{
    public sealed class PrincipalTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Email_is_lower_cased_and_trimmed()
        {
            var principal = Principal.Create("sub-1", PrincipalKind.User, "  UserA@Example.COM ", true, " User A ", Now);

            principal.Email.Should().Be("usera@example.com");
            principal.EmailVerified.Should().BeTrue();
            principal.DisplayName.Should().Be("User A");
        }

        [Fact]
        public void Missing_email_is_never_verified()
        {
            var principal = Principal.Create("sub-1", PrincipalKind.Service, null, true, null, Now);

            principal.Email.Should().BeNull();
            principal.EmailVerified.Should().BeFalse();
        }

        [Fact]
        public void UpdateProfile_reports_changes_only()
        {
            var principal = Principal.Create("sub-1", PrincipalKind.User, "a@x.io", true, "A", Now);

            principal.UpdateProfile("A@X.IO", true, "A").Should().BeFalse();
            principal.UpdateProfile("a@x.io", false, "A").Should().BeTrue();
            principal.EmailVerified.Should().BeFalse();
        }

        [Fact]
        public void Touch_respects_the_minimum_interval()
        {
            var principal = Principal.Create("sub-1", PrincipalKind.User, null, false, null, Now);

            principal.Touch(Now.AddMinutes(1), TimeSpan.FromMinutes(5)).Should().BeFalse();
            principal.Touch(Now.AddMinutes(6), TimeSpan.FromMinutes(5)).Should().BeTrue();
            principal.LastSeenAt.Should().Be(Now.AddMinutes(6));
        }

        [Fact]
        public void Sub_is_required()
        {
            FluentActions.Invoking(() => Principal.Create(" ", PrincipalKind.User, null, false, null, Now))
                .Should().Throw<ArgumentException>();
        }
    }
}
