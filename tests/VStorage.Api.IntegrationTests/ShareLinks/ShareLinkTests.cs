using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VStorage.Api.Auth;
using VStorage.Api.IntegrationTests.Objects;
using VStorage.Application.Abstractions;

namespace VStorage.Api.IntegrationTests.ShareLinks
{
    /// <summary>Share links (spec §5.8): rights, public and addressed redemption, counting, expiry, revoke, received.</summary>
    [Collection(ApiCollection.Name)]
    public sealed class ShareLinkTests(ApiFactory factory)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Public_link_serves_the_file_without_sign_in_and_counts_only_downloads_from_byte_0()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var created = await owner.UploadAsync(bucketId, "a.txt", "0123456789", "shared");

            var link = await CreateLinkAsync(owner, created.Id(), new { maxDownloads = 2 });
            link.GetProperty("kind").GetString().Should().Be("public");
            var url = link.GetProperty("url").GetString()!;
            url.Should().MatchRegex("^https://files.test/s/[0-9A-Za-z]{12}$");
            var path = new Uri(url).AbsolutePath;
            using var anonymous = Browser();

            using (var first = await anonymous.GetAsync(path, Ct))
            {
                first.StatusCode.Should().Be(HttpStatusCode.OK);
                (await first.Content.ReadAsStringAsync(Ct)).Should().Be("0123456789");
                first.Headers.GetValues("X-Robots-Tag").Should().Equal("noindex");
            }

            (await RangeAsync(anonymous, path, 5)).StatusCode.Should().Be(HttpStatusCode.PartialContent);
            (await anonymous.SendAsync(new HttpRequestMessage(HttpMethod.Head, path), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await anonymous.GetAsync(path, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            using var exhausted = await anonymous.GetAsync(path, Ct);
            exhausted.StatusCode.Should().Be(HttpStatusCode.Gone);
            (await CodeAsync(exhausted)).Should().Be("exhausted");
            (await RangeAsync(anonymous, path, 5)).StatusCode.Should().Be(HttpStatusCode.PartialContent);

            var listed = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/objects/{created.Id()}/share-links", Ct);
            listed[0].GetProperty("downloads").GetInt32().Should().Be(2);
            listed[0].GetProperty("state").GetString().Should().Be("exhausted");
        }

        [Fact]
        public async Task Addressed_link_needs_sign_in_and_a_recipient_session()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var (recipient, recipientSub) = await CreateUserWithSubAsync();
            var (stranger, strangerSub) = await CreateUserWithSubAsync();
            using (recipient)
            using (stranger)
            {
                var bucketId = await owner.CreateBucketAsync();
                var created = await owner.UploadAsync(bucketId, "secret.txt", "for you", "private");
                var link = await CreateLinkAsync(owner, created.Id(), new { recipientUserIds = new[] { recipient.Id } });
                link.GetProperty("kind").GetString().Should().Be("addressed");
                var path = new Uri(link.GetProperty("url").GetString()!).AbsolutePath;
                path.Should().MatchRegex("^/s/[0-9A-Za-z]{8}$");

                using var anonymous = Browser();
                using var challenge = await anonymous.GetAsync(path, Ct);
                challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
                challenge.Headers.Location!.ToString().Should().StartWith(ApiFactory.KeycloakAuthorizeUrl).And.Contain("code_challenge=");

                using var asRecipient = Browser(SessionCookie(recipientSub));
                using var served = await asRecipient.GetAsync(path, Ct);
                served.StatusCode.Should().Be(HttpStatusCode.OK);
                (await served.Content.ReadAsStringAsync(Ct)).Should().Be("for you");

                using var asStranger = Browser(SessionCookie(strangerSub));
                (await asStranger.GetAsync(path, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

                var received = await recipient.Client.GetFromJsonAsync<JsonElement>("/api/v1/share-links/received", Ct);
                received.EnumerateArray().Should().ContainSingle()
                    .Which.GetProperty("fileName").GetString().Should().Be("secret.txt");
                (await stranger.Client.GetFromJsonAsync<JsonElement>("/api/v1/share-links/received", Ct)).GetArrayLength().Should().Be(0);
            }
        }

        [Fact]
        public async Task Expired_link_is_410_and_revoked_link_is_404()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var created = await owner.UploadAsync(bucketId, "a.txt", "a", "shared");
            var expiring = await CreateLinkAsync(owner, created.Id(), new { });
            var revoked = await CreateLinkAsync(owner, created.Id(), new { });
            using var anonymous = Browser();

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IVStorageDbContext>();
                var id = expiring.Id();
                await db.ShareLinks.Where(l => l.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
            }

            using var gone = await anonymous.GetAsync(new Uri(expiring.GetProperty("url").GetString()!).AbsolutePath, Ct);
            gone.StatusCode.Should().Be(HttpStatusCode.Gone);
            (await CodeAsync(gone)).Should().Be("expired");

            (await owner.Client.DeleteAsync($"/api/v1/share-links/{revoked.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await anonymous.GetAsync(new Uri(revoked.GetProperty("url").GetString()!).AbsolutePath, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.Client.DeleteAsync($"/api/v1/share-links/{revoked.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await anonymous.GetAsync("/s/doesNotExist12", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Creation_follows_the_permission_matrix_and_validates_input()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var admin = await factory.CreateUserAsync();
            using var member = await factory.CreateUserAsync();
            using var viewer = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, admin, "admin");
            await owner.AddMemberAsync(bucketId, member, "member");
            await owner.AddMemberAsync(bucketId, viewer, "viewer");
            var ownersShared = await owner.UploadAsync(bucketId, "shared.txt", "s", "shared");
            var ownersPrivate = await owner.UploadAsync(bucketId, "private.txt", "p", "private");
            var membersOwn = await member.UploadAsync(bucketId, "mine.txt", "m", "private");

            (await PostLinkAsync(member, membersOwn.Id(), new { })).StatusCode.Should().Be(HttpStatusCode.Created);
            (await PostLinkAsync(admin, ownersShared.Id(), new { })).StatusCode.Should().Be(HttpStatusCode.Created);
            (await PostLinkAsync(member, ownersShared.Id(), new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await PostLinkAsync(viewer, ownersShared.Id(), new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await PostLinkAsync(admin, ownersPrivate.Id(), new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);

            (await PostLinkAsync(owner, ownersShared.Id(), new { recipientUserIds = new[] { Guid.NewGuid() } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await PostLinkAsync(owner, ownersShared.Id(), new { expiresAt = DateTimeOffset.UtcNow.AddYears(2) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await PostLinkAsync(owner, ownersShared.Id(), new { expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await PostLinkAsync(owner, ownersShared.Id(), new { maxDownloads = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Owner/admin see every link of a shared object; the member only their own.
            (await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/objects/{ownersShared.Id()}/share-links", Ct)).GetArrayLength().Should().Be(1);
            var membersLinks = await member.Client.GetFromJsonAsync<JsonElement>($"/api/v1/objects/{membersOwn.Id()}/share-links", Ct);
            membersLinks.GetArrayLength().Should().Be(1);

            // The member's link is on a private object: invisible to the bucket admin, so revoking it is 404.
            var membersLinkId = membersLinks[0].Id();
            (await admin.Client.DeleteAsync($"/api/v1/share-links/{membersLinkId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        private HttpClient Browser(string? cookie = null)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
            if (cookie is not null)
            {
                client.DefaultRequestHeaders.Add("Cookie", cookie);
            }

            return client;
        }

        // A /s session as the sign-in would create it: the app's own cookie format with only "sub".
        private string SessionCookie(string subject)
        {
            var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(ShareSignIn.CookieScheme);
            var identity = new ClaimsIdentity([new Claim(ShareSignIn.SubjectClaim, subject)], ShareSignIn.CookieScheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties
            {
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
            }, ShareSignIn.CookieScheme);
            return $"{ShareSignIn.CookieName}={options.TicketDataFormat.Protect(ticket)}";
        }

        private async Task<(TestUser User, string Subject)> CreateUserWithSubAsync()
        {
            var subject = Guid.NewGuid().ToString();
            var client = factory.CreateClient(TestTokens.Create(subject));
            var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);
            return (new TestUser { Id = me.GetProperty("id").GetGuid(), Client = client }, subject);
        }

        private static async Task<HttpResponseMessage> RangeAsync(HttpClient client, string path, long from)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Range = new RangeHeaderValue(from, null);
            return await client.SendAsync(request, Ct);
        }

        private static async Task<HttpResponseMessage> PostLinkAsync(TestUser user, Guid objectId, object body) =>
            await user.Client.PostAsJsonAsync($"/api/v1/objects/{objectId}/share-links", body, Ct);

        private static async Task<JsonElement> CreateLinkAsync(TestUser user, Guid objectId, object body)
        {
            using var response = await PostLinkAsync(user, objectId, body);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }

        private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
    }
}
