using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VStorage.Api.IntegrationTests.Auth
{
    /// <summary>Token validation, lazy provisioning, platform roles and user lookup (spec §5.1, §5.3).</summary>
    [Collection(ApiCollection.Name)]
    public sealed class AuthTests(ApiFactory factory)
    {
        private static readonly Dictionary<string, string[]> AdminRoles = new() { ["vstorage"] = ["vstorage-admin"] };

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Without_token_api_returns_401()
        {
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/api/v1/me", Ct);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Should().ContainSingle(h => h.Scheme == "Bearer");
        }

        [Fact]
        public async Task Token_for_another_audience_is_rejected()
        {
            using var client = factory.CreateClient(TestTokens.Create(NewSub(), audiences: ["vgplanner"]));

            using var response = await client.GetAsync("/api/v1/me", Ct);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.ToString().Should().Contain("invalid_token");
        }

        [Fact]
        public async Task Audience_array_containing_vstorage_is_accepted()
        {
            using var client = factory.CreateClient(TestTokens.Create(NewSub(), audiences: ["account", "vstorage"]));

            using var response = await client.GetAsync("/api/v1/me", Ct);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Expired_token_is_rejected()
        {
            using var client = factory.CreateClient(TestTokens.Create(NewSub(), expires: DateTime.UtcNow.AddMinutes(-5)));

            using var response = await client.GetAsync("/api/v1/me", Ct);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task First_call_provisions_the_principal_and_later_calls_reuse_it()
        {
            var sub = NewSub();
            var first = await GetMeAsync(TestTokens.Create(sub, email: "UserA@Example.COM", emailVerified: true, name: "User A"));
            var second = await GetMeAsync(TestTokens.Create(sub, email: "usera-new@example.com", emailVerified: true, name: "User A"));

            first.GetProperty("id").GetGuid().Should().Be(second.GetProperty("id").GetGuid());
            first.GetProperty("kind").GetString().Should().Be("user");
            first.GetProperty("email").GetString().Should().Be("usera@example.com");
            first.GetProperty("clientId").GetString().Should().Be("vgplanner-api");
            second.GetProperty("email").GetString().Should().Be("usera-new@example.com");
        }

        [Fact]
        public async Task Service_account_token_creates_a_service_principal()
        {
            var me = await GetMeAsync(TestTokens.ServiceAccount("vgplanner-api", NewSub()));

            me.GetProperty("kind").GetString().Should().Be("service");
            me.GetProperty("displayName").GetString().Should().Be("service-account-vgplanner-api");
        }

        [Fact]
        public async Task Platform_roles_come_only_from_the_vstorage_client()
        {
            var admin = await GetMeAsync(TestTokens.Create(NewSub(), clientRoles: AdminRoles));
            var creator = await GetMeAsync(TestTokens.Create(NewSub(), clientRoles: new Dictionary<string, string[]> { ["vstorage"] = ["bucket-creator"] }));
            var otherClient = await GetMeAsync(TestTokens.Create(NewSub(), clientRoles: new Dictionary<string, string[]> { ["vgplanner-api"] = ["vstorage-admin", "bucket-creator"] }));

            admin.GetProperty("isPlatformAdmin").GetBoolean().Should().BeTrue();
            admin.GetProperty("canCreateBuckets").GetBoolean().Should().BeTrue();
            creator.GetProperty("isPlatformAdmin").GetBoolean().Should().BeFalse();
            creator.GetProperty("canCreateBuckets").GetBoolean().Should().BeTrue();
            otherClient.GetProperty("isPlatformAdmin").GetBoolean().Should().BeFalse();
            otherClient.GetProperty("canCreateBuckets").GetBoolean().Should().BeFalse();
        }

        [Fact]
        public async Task Lookup_finds_only_exact_verified_emails()
        {
            var unique = Guid.NewGuid().ToString("N")[..8];
            var verifiedEmail = $"lookup-{unique}@example.com";
            var target = await GetMeAsync(TestTokens.Create(NewSub(), email: verifiedEmail, emailVerified: true, name: "Target"));
            await GetMeAsync(TestTokens.Create(NewSub(), email: $"unverified-{unique}@example.com", emailVerified: false));

            using var client = factory.CreateClient(TestTokens.Create(NewSub()));

            var found = await client.GetFromJsonAsync<JsonElement>($"/api/v1/users/lookup?email=LOOKUP-{unique}@Example.com", Ct);
            found.GetProperty("id").GetGuid().Should().Be(target.GetProperty("id").GetGuid());
            found.GetProperty("displayName").GetString().Should().Be("Target");

            (await client.GetAsync($"/api/v1/users/lookup?email=unverified-{unique}@example.com", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.GetAsync($"/api/v1/users/lookup?email=lookup-{unique}@example", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Invalid_lookup_email_returns_problem_details_with_type_and_request_id()
        {
            using var client = factory.CreateClient(TestTokens.Create(NewSub()));

            using var response = await client.GetAsync("/api/v1/users/lookup?email=not-an-email", Ct);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            problem.GetProperty("type").GetString().Should().Be("https://files.test/errors/invalid-email");
            problem.GetProperty("requestId").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Lookup_is_rate_limited_per_principal()
        {
            using var client = factory.CreateClient(TestTokens.Create(NewSub()));

            HttpResponseMessage? last = null;
            for (var i = 0; i < 31; i++)
            {
                last?.Dispose();
                last = await client.GetAsync("/api/v1/users/lookup?email=nobody@example.com", Ct);
            }

            last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            last.Headers.RetryAfter.Should().NotBeNull();
            last.Dispose();
        }

        private static string NewSub() => Guid.NewGuid().ToString();

        private async Task<JsonElement> GetMeAsync(string token)
        {
            using var client = factory.CreateClient(token);
            using var response = await client.GetAsync("/api/v1/me", Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }
}
