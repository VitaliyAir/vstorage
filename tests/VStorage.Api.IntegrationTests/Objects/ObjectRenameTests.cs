using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VStorage.Api.IntegrationTests.Objects
{
    /// <summary>Renaming the keys of several own objects in one step (a client renames or moves a folder).</summary>
    [Collection(ApiCollection.Name)]
    public sealed class ObjectRenameTests(ApiFactory factory)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Rename_changes_the_keys_and_reports_unknown_ids()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var a = await owner.UploadAsync(bucketId, "Library/Physcs/a.pdf", "a", "private");
            var b = await owner.UploadAsync(bucketId, "Library/Physcs/Classic/b.pdf", "b", "private");
            var unknown = Guid.NewGuid();

            using var response = await RenameAsync(owner, bucketId,
                (a.Id(), "Library/Physics/a.pdf"), (b.Id(), "Library/Physics/Classic/b.pdf"), (unknown, "Library/Physics/c.pdf"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            result.GetProperty("renamed").GetInt32().Should().Be(2);
            result.GetProperty("missing")[0].GetGuid().Should().Be(unknown);
            (await owner.DownloadTextAsync($"/api/v1/buckets/{bucketId}/objects/content?key=Library/Physics/Classic/b.pdf")).Should().Be("b");
            (await owner.Client.GetAsync($"/api/v1/buckets/{bucketId}/objects/content?key=Library/Physcs/a.pdf", Ct))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);

            // The same request again: nothing left to change.
            using var again = await RenameAsync(owner, bucketId, (a.Id(), "Library/Physics/a.pdf"));
            (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("unchanged").GetInt32().Should().Be(1);
        }

        [Fact]
        public async Task A_taken_key_changes_nothing_and_members_cannot_rename_your_objects()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var member = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, member, "member");
            var a = await owner.UploadAsync(bucketId, "x/a.txt", "a", "private");
            var b = await owner.UploadAsync(bucketId, "x/b.txt", "b", "private");
            await owner.UploadAsync(bucketId, "y/a.txt", "other", "private");

            using var taken = await RenameAsync(owner, bucketId, (b.Id(), "y/b.txt"), (a.Id(), "y/a.txt"));
            taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await owner.DownloadTextAsync($"/api/v1/buckets/{bucketId}/objects/content?key=x/b.txt")).Should().Be("b");

            using var byMember = await RenameAsync(member, bucketId, (a.Id(), "z/a.txt"));
            var result = await byMember.Content.ReadFromJsonAsync<JsonElement>(Ct);
            result.GetProperty("renamed").GetInt32().Should().Be(0);
            result.GetProperty("missing")[0].GetGuid().Should().Be(a.Id());
        }

        private static Task<HttpResponseMessage> RenameAsync(TestUser user, Guid bucketId, params (Guid Id, string Key)[] items) =>
            user.Client.PostAsJsonAsync(
                $"/api/v1/buckets/{bucketId}/objects/rename",
                new { items = items.Select(i => new { objectId = i.Id, key = i.Key }) },
                Ct);
    }
}
