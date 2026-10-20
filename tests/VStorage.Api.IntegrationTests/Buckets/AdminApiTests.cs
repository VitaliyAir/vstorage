using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VStorage.Api.IntegrationTests.Buckets
{
    /// <summary>Admin API (vstorage-admin): any owner, no membership needed, never object access.</summary>
    [Collection(ApiCollection.Name)]
    public sealed class AdminApiTests(ApiFactory factory)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Admin_api_requires_the_vstorage_admin_role()
        {
            using var creator = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);

            (await creator.Client.GetAsync("/api/v1/admin/users", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await creator.Client.GetAsync("/api/v1/admin/buckets", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task Admin_finds_users_and_service_accounts()
        {
            using var admin = await factory.CreateUserAsync(vstorageRoles: TestUsers.PlatformAdmin);
            var unique = Guid.NewGuid().ToString("N")[..8];
            using var user = await factory.CreateUserAsync(email: $"find-{unique}@example.com");
            using var service = await factory.CreateServiceAccountAsync($"consumer-{unique}");

            var byEmail = await admin.Client.GetFromJsonAsync<JsonElement[]>($"/api/v1/admin/users?search=FIND-{unique}", Ct);
            byEmail.Should().ContainSingle().Which.GetProperty("id").GetGuid().Should().Be(user.Id);

            var byName = await admin.Client.GetFromJsonAsync<JsonElement[]>($"/api/v1/admin/users?search=service-account-consumer-{unique}", Ct);
            var found = byName.Should().ContainSingle().Which;
            found.GetProperty("id").GetGuid().Should().Be(service.Id);
            found.GetProperty("kind").GetString().Should().Be("service");
        }

        [Fact]
        public async Task Admin_creates_a_bucket_for_a_service_account_without_becoming_member()
        {
            using var admin = await factory.CreateUserAsync(vstorageRoles: TestUsers.PlatformAdmin);
            using var service = await factory.CreateServiceAccountAsync("vgplanner-api");
            var name = TestUsers.NewBucketName();

            using var response = await admin.Client.PostAsJsonAsync("/api/v1/admin/buckets", new { name, ownerId = service.Id }, Ct);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var bucketId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

            (await service.Client.GetFromJsonAsync<JsonElement>($"/api/v1/buckets/{bucketId}", Ct)).GetProperty("role").GetString().Should().Be("owner");
            (await admin.Client.GetAsync($"/api/v1/buckets/{bucketId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await admin.Client.GetAsync($"/api/v1/admin/buckets/{bucketId}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            var listed = await admin.Client.GetFromJsonAsync<JsonElement[]>($"/api/v1/admin/buckets?name={name}", Ct);
            listed.Should().ContainSingle().Which.GetProperty("ownerId").GetGuid().Should().Be(service.Id);
        }

        [Fact]
        public async Task Admin_manages_members_transfers_and_deletes_any_bucket()
        {
            using var admin = await factory.CreateUserAsync(vstorageRoles: TestUsers.PlatformAdmin);
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var newOwner = await factory.CreateUserAsync();
            using var user = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();

            (await admin.Client.PostAsJsonAsync($"/api/v1/admin/buckets/{bucketId}/members", new { userId = user.Id, role = "admin" }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Created);

            using var transfer = await admin.Client.PostAsJsonAsync($"/api/v1/admin/buckets/{bucketId}/transfer", new { newOwnerId = newOwner.Id }, Ct);
            transfer.StatusCode.Should().Be(HttpStatusCode.OK);
            (await newOwner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/buckets/{bucketId}", Ct)).GetProperty("role").GetString().Should().Be("owner");

            (await admin.Client.DeleteAsync($"/api/v1/admin/buckets/{bucketId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await newOwner.Client.GetAsync($"/api/v1/buckets/{bucketId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
