using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VStorage.Infrastructure.Persistence;

namespace VStorage.Api.IntegrationTests.Buckets
{
    /// <summary>Bucket create / read / quota / delete / transfer (spec §5.4).</summary>
    [Collection(ApiCollection.Name)]
    public sealed class BucketApiTests(ApiFactory factory)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Creating_a_bucket_requires_the_bucket_creator_role()
        {
            using var user = await factory.CreateUserAsync();

            using var response = await user.Client.PostAsJsonAsync("/api/v1/buckets", new { name = TestUsers.NewBucketName() }, Ct);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task Creator_becomes_owner_and_BucketCreated_carries_request_context()
        {
            using var creator = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var name = TestUsers.NewBucketName();

            using var response = await creator.Client.PostAsJsonAsync("/api/v1/buckets", new { name, quotaBytes = 1000 }, Ct);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var bucket = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            var bucketId = bucket.GetProperty("id").GetGuid();
            response.Headers.Location!.ToString().Should().Be($"/api/v1/buckets/{bucketId}");
            bucket.GetProperty("role").GetString().Should().Be("owner");
            bucket.GetProperty("quotaBytes").GetInt64().Should().Be(1000);

            var mine = await creator.Client.GetFromJsonAsync<JsonElement[]>("/api/v1/buckets", Ct);
            mine.Should().ContainSingle(b => b.GetProperty("id").GetGuid() == bucketId);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VStorageDbContext>();
            var messages = await db.OutboxMessages.Where(m => m.Type == "BucketCreated").ToListAsync(Ct);
            using var payload = JsonDocument.Parse(messages.Single(m => m.Payload.Contains(bucketId.ToString())).Payload);
            var context = payload.RootElement.GetProperty("context");
            context.GetProperty("principalId").GetString().Should().Be(creator.Id.ToString());
            context.GetProperty("clientId").GetString().Should().Be("vgplanner-api");
            context.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Names_are_validated_and_unique_per_owner()
        {
            using var first = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var second = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var name = TestUsers.NewBucketName();

            using var invalid = await first.Client.PostAsJsonAsync("/api/v1/buckets", new { name = "Bad_Name" }, Ct);
            invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ProblemTypeAsync(invalid)).Should().EndWith("/errors/invalid-bucket-name");

            await first.CreateBucketAsync(name);
            using var duplicate = await first.Client.PostAsJsonAsync("/api/v1/buckets", new { name }, Ct);
            duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

            using var otherOwner = await second.Client.PostAsJsonAsync("/api/v1/buckets", new { name }, Ct);
            otherOwner.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        [Fact]
        public async Task Non_member_gets_404_and_member_sees_own_role()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var member = await factory.CreateUserAsync();
            using var stranger = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, member, "member");

            (await stranger.Client.GetAsync($"/api/v1/buckets/{bucketId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var seen = await member.Client.GetFromJsonAsync<JsonElement>($"/api/v1/buckets/{bucketId}", Ct);
            seen.GetProperty("role").GetString().Should().Be("member");
            seen.GetProperty("memberCount").GetInt32().Should().Be(1);
        }

        [Fact]
        public async Task Only_the_owner_changes_quota_and_deletes()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var admin = await factory.CreateUserAsync();
            using var stranger = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, admin, "admin");

            (await admin.Client.PatchAsJsonAsync($"/api/v1/buckets/{bucketId}", new { quotaBytes = 10 }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await stranger.Client.PatchAsJsonAsync($"/api/v1/buckets/{bucketId}", new { quotaBytes = 10 }, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var updated = await (await owner.Client.PatchAsJsonAsync($"/api/v1/buckets/{bucketId}", new { quotaBytes = 10 }, Ct))
                .Content.ReadFromJsonAsync<JsonElement>(Ct);
            updated.GetProperty("quotaBytes").GetInt64().Should().Be(10);

            (await admin.Client.DeleteAsync($"/api/v1/buckets/{bucketId}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await owner.Client.DeleteAsync($"/api/v1/buckets/{bucketId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await owner.Client.GetAsync($"/api/v1/buckets/{bucketId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.Client.GetFromJsonAsync<JsonElement[]>("/api/v1/buckets", Ct))
                .Should().NotContain(b => b.GetProperty("id").GetGuid() == bucketId);
        }

        [Fact]
        public async Task Transfer_requires_a_member_and_keeps_previous_owner_as_admin()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var member = await factory.CreateUserAsync();
            using var stranger = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, member, "member");

            using var toStranger = await owner.Client.PostAsJsonAsync($"/api/v1/buckets/{bucketId}/transfer", new { newOwnerId = stranger.Id }, Ct);
            toStranger.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemTypeAsync(toStranger)).Should().EndWith("/errors/new-owner-not-member");

            using var toMember = await owner.Client.PostAsJsonAsync($"/api/v1/buckets/{bucketId}/transfer", new { newOwnerId = member.Id }, Ct);
            toMember.StatusCode.Should().Be(HttpStatusCode.OK);

            (await member.Client.GetFromJsonAsync<JsonElement>($"/api/v1/buckets/{bucketId}", Ct)).GetProperty("role").GetString().Should().Be("owner");
            (await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/buckets/{bucketId}", Ct)).GetProperty("role").GetString().Should().Be("admin");
        }

        private static async Task<string> ProblemTypeAsync(HttpResponseMessage response) =>
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("type").GetString()!;
    }
}
