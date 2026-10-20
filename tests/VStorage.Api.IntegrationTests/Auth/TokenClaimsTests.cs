using System.Security.Claims;
using VStorage.Api.Auth;
using VStorage.Domain.Principals;

namespace VStorage.Api.IntegrationTests.Auth
{
    /// <summary>Unit tests of the claim reading (no host needed).</summary>
    public sealed class TokenClaimsTests
    {
        [Fact]
        public void User_token_is_read()
        {
            var claims = TokenClaims.ToPrincipalClaims(Principal(
                ("sub", "s1"), ("preferred_username", "usera"), ("name", "User A"), ("email", "a@x.io"), ("email_verified", "true")));

            claims.Should().BeEquivalentTo(new { Subject = "s1", Kind = PrincipalKind.User, Email = "a@x.io", EmailVerified = true, DisplayName = "User A" });
        }

        [Fact]
        public void Service_account_is_detected_by_user_name()
        {
            var claims = TokenClaims.ToPrincipalClaims(Principal(("sub", "s2"), ("preferred_username", "service-account-vgplanner-api")));

            claims!.Kind.Should().Be(PrincipalKind.Service);
            claims.DisplayName.Should().Be("service-account-vgplanner-api");
        }

        [Fact]
        public void Missing_or_false_email_verified_means_unverified()
        {
            TokenClaims.ToPrincipalClaims(Principal(("sub", "s3"), ("email", "a@x.io")))!.EmailVerified.Should().BeFalse();
            TokenClaims.ToPrincipalClaims(Principal(("sub", "s3"), ("email_verified", "false")))!.EmailVerified.Should().BeFalse();
        }

        [Fact]
        public void Token_without_sub_gives_no_identity()
        {
            TokenClaims.ToPrincipalClaims(Principal(("preferred_username", "x"))).Should().BeNull();
        }

        [Fact]
        public void Client_id_prefers_azp()
        {
            TokenClaims.ClientId(Principal(("azp", "vgplanner-api"), ("client_id", "other"))).Should().Be("vgplanner-api");
            TokenClaims.ClientId(Principal(("client_id", "vgplanner-api"))).Should().Be("vgplanner-api");
        }

        private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
            new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));
    }
}
