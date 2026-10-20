using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VStorage.Api.IntegrationTests.Buckets
{
    /// <summary>Members and the member part of the permission matrix (spec §3, §5.5).</summary>
    [Collection(ApiCollection.Name)]
    public sealed class MemberApiTests(ApiFactory factory)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Only_existing_users_can_be_added_once()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var user = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();

            (await AddAsync(owner, bucketId, Guid.NewGuid(), "member")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await AddAsync(owner, bucketId, user.Id, "owner")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var added = await AddAsync(owner, bucketId, user.Id, "viewer");
            added.StatusCode.Should().Be(HttpStatusCode.Created);
            (await added.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("role").GetString().Should().Be("viewer");

            (await AddAsync(owner, bucketId, user.Id, "member")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task Bucket_admin_manages_members_and_viewers_but_not_admins()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var admin = await factory.CreateUserAsync();
            using var otherAdmin = await factory.CreateUserAsync();
            using var user = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, admin, "admin");
            await owner.AddMemberAsync(bucketId, otherAdmin, "admin");

            (await AddAsync(admin, bucketId, user.Id, "admin")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await AddAsync(admin, bucketId, user.Id, "member")).StatusCode.Should().Be(HttpStatusCode.Created);
            (await admin.Client.PatchAsJsonAsync($"/api/v1/buckets/{bucketId}/members/{user.Id}", new { role = "admin" }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await admin.Client.DeleteAsync($"/api/v1/buckets/{bucketId}/members/{otherAdmin.Id}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await admin.Client.DeleteAsync($"/api/v1/buckets/{bucketId}/members/{user.Id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task Members_and_viewers_cannot_manage_but_can_list()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var member = await factory.CreateUserAsync();
            using var viewer = await factory.CreateUserAsync();
            using var user = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, member, "member");
            await owner.AddMemberAsync(bucketId, viewer, "viewer");

            (await AddAsync(member, bucketId, user.Id, "viewer")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await AddAsync(viewer, bucketId, user.Id, "viewer")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var members = await viewer.Client.GetFromJsonAsync<JsonElement[]>($"/api/v1/buckets/{bucketId}/members", Ct);
            members!.Select(m => m.GetProperty("role").GetString()).Should().Equal("owner", "member", "viewer");
        }

        [Fact]
        public async Task Owner_role_cannot_be_changed_and_owner_cannot_leave()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();

            (await owner.Client.PatchAsJsonAsync($"/api/v1/buckets/{bucketId}/members/{owner.Id}", new { role = "admin" }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await owner.Client.DeleteAsync($"/api/v1/buckets/{bucketId}/members/me", Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task Member_can_leave_and_then_loses_access()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var member = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, member, "member");

            (await member.Client.DeleteAsync($"/api/v1/buckets/{bucketId}/members/me", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

            (await member.Client.GetAsync($"/api/v1/buckets/{bucketId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await member.Client.DeleteAsync($"/api/v1/buckets/{bucketId}/members/me", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        private static Task<HttpResponseMessage> AddAsync(TestUser manager, Guid bucketId, Guid userId, string role) =>
            manager.Client.PostAsJsonAsync($"/api/v1/buckets/{bucketId}/members", new { userId, role }, Ct);
    }
}
