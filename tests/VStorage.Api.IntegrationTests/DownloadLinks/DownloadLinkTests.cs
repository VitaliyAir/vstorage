using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VStorage.Api.IntegrationTests.Objects;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.DownloadLinks;

namespace VStorage.Api.IntegrationTests.DownloadLinks
{
    /// <summary>Download links (spec §5.7): creation rules, anonymous /d download, 404 / 410 rules, SEC-7 headers.</summary>
    [Collection(ApiCollection.Name)]
    public sealed class DownloadLinkTests(ApiFactory factory)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Link_downloads_without_a_token_with_range_head_and_robots_headers()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var created = await owner.UploadAsync(bucketId, "docs/a.txt", "0123456789");

            var link = await CreateLinkAsync(owner, created.Id(), new { expiresInSeconds = 120 });
            link.GetProperty("url").GetString().Should().StartWith("https://files.test/d/");
            link.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(120), TimeSpan.FromSeconds(10));

            using var anonymous = factory.CreateClient();
            var path = PathOf(link);
            using var get = await anonymous.GetAsync(path, Ct);
            get.StatusCode.Should().Be(HttpStatusCode.OK);
            (await get.Content.ReadAsStringAsync(Ct)).Should().Be("0123456789");
            get.Headers.GetValues("X-Robots-Tag").Should().Equal("noindex");
            get.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
            get.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");

            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, path);
            rangeRequest.Headers.Range = new RangeHeaderValue(2, 5);
            using var range = await anonymous.SendAsync(rangeRequest, Ct);
            range.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            (await range.Content.ReadAsStringAsync(Ct)).Should().Be("2345");

            using var head = await anonymous.SendAsync(new HttpRequestMessage(HttpMethod.Head, path), Ct);
            head.StatusCode.Should().Be(HttpStatusCode.OK);
            head.Content.Headers.ContentLength.Should().Be(10);
        }

        [Fact]
        public async Task Inline_is_honoured_only_for_safe_types()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var image = await owner.UploadAsync(bucketId, "a.png", "png", contentType: "image/png");
            var page = await owner.UploadAsync(bucketId, "a.html", "<script>alert(1)</script>", contentType: "text/html");
            using var anonymous = factory.CreateClient();

            using var imageResponse = await anonymous.GetAsync(PathOf(await CreateLinkAsync(owner, image.Id(), new { disposition = "inline" })), Ct);
            imageResponse.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");

            using var pageResponse = await anonymous.GetAsync(PathOf(await CreateLinkAsync(owner, page.Id(), new { disposition = "inline" })), Ct);
            pageResponse.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        }

        [Fact]
        public async Task Creation_needs_read_access_and_valid_parameters()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var viewer = await factory.CreateUserAsync();
            using var stranger = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, viewer, "viewer");
            var shared = await owner.UploadAsync(bucketId, "shared.txt", "s", "shared");
            var secret = await owner.UploadAsync(bucketId, "secret.txt", "p", "private");

            (await PostLinkAsync(viewer, shared.Id(), new { })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PostLinkAsync(viewer, secret.Id(), new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await PostLinkAsync(stranger, shared.Id(), new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await PostLinkAsync(owner, shared.Id(), new { expiresInSeconds = 3601 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await PostLinkAsync(owner, shared.Id(), new { expiresInSeconds = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await PostLinkAsync(owner, shared.Id(), new { disposition = "download" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Link_stops_working_when_the_object_is_deleted_or_the_creator_loses_access()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var member = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, member, "member");
            var kept = await owner.UploadAsync(bucketId, "kept.txt", "k", "shared");
            var deleted = await owner.UploadAsync(bucketId, "deleted.txt", "d", "shared");
            var memberLink = PathOf(await CreateLinkAsync(member, kept.Id(), new { }));
            var deletedLink = PathOf(await CreateLinkAsync(owner, deleted.Id(), new { }));
            using var anonymous = factory.CreateClient();

            (await anonymous.GetAsync(memberLink, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await owner.Client.DeleteAsync($"/api/v1/buckets/{bucketId}/members/{member.Id}", Ct)).EnsureSuccessStatusCode();
            (await anonymous.GetAsync(memberLink, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            (await owner.Client.DeleteAsync($"/api/v1/objects/{deleted.Id()}", Ct)).EnsureSuccessStatusCode();
            (await anonymous.GetAsync(deletedLink, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Tampered_tokens_are_404_and_expired_tokens_are_410()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var created = await owner.UploadAsync(bucketId, "a.txt", "a");
            var path = PathOf(await CreateLinkAsync(owner, created.Id(), new { }));
            using var anonymous = factory.CreateClient();

            var tampered = path[..^3] + (path[^3] == 'A' ? 'B' : 'A') + path[^2..];
            (await anonymous.GetAsync(tampered, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await anonymous.GetAsync("/d/not-a-token", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // An authentic token whose expiry has passed (minted with the app's own key ring).
            var protector = factory.Services.GetRequiredService<IDownloadTokenProtector>();
            var expired = protector.Protect(new DownloadToken(created.Id(), owner.Id, false, DateTimeOffset.UtcNow.AddSeconds(-1)));
            using var gone = await anonymous.GetAsync($"/d/{expired}", Ct);
            gone.StatusCode.Should().Be(HttpStatusCode.Gone);
            (await gone.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be("expired");
        }

        private static async Task<HttpResponseMessage> PostLinkAsync(TestUser user, Guid objectId, object body) =>
            await user.Client.PostAsJsonAsync($"/api/v1/objects/{objectId}/download-link", body, Ct);

        private static async Task<JsonElement> CreateLinkAsync(TestUser user, Guid objectId, object body)
        {
            using var response = await PostLinkAsync(user, objectId, body);
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }

        // The test server ignores the host: /d/{token} of the returned https://files.test/... URL.
        private static string PathOf(JsonElement link) => new Uri(link.GetProperty("url").GetString()!).AbsolutePath;
    }
}
