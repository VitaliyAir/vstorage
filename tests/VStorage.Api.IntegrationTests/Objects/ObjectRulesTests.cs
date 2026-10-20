using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VStorage.Api.IntegrationTests.Objects
{
    /// <summary>Visibility, key resolution, FR-O6 write semantics and object permissions (spec §3, §5.6).</summary>
    [Collection(ApiCollection.Name)]
    public sealed class ObjectRulesTests(ApiFactory factory)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Private_objects_are_invisible_to_everyone_else_including_the_bucket_owner()
        {
            var (owner, member, viewer, bucketId) = await SetUpAsync();
            using (owner)
            using (member)
            using (viewer)
            {
                var secret = await member.UploadAsync(bucketId, "secret.txt", "mine", "private");
                var common = await member.UploadAsync(bucketId, "common.txt", "ours", "shared");

                (await owner.Client.GetAsync($"/api/v1/objects/{secret.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
                (await owner.Client.GetAsync($"/api/v1/objects/{secret.Id()}/content", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
                (await owner.Client.GetAsync($"/api/v1/buckets/{bucketId}/objects/content?key=secret.txt", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

                (await viewer.DownloadTextAsync($"/api/v1/objects/{common.Id()}/content")).Should().Be("ours");
            }
        }

        [Fact]
        public async Task Strangers_get_404_and_viewers_cannot_upload()
        {
            var (owner, member, viewer, bucketId) = await SetUpAsync();
            using var stranger = await factory.CreateUserAsync();
            using (owner)
            using (member)
            using (viewer)
            {
                var common = await member.UploadAsync(bucketId, "common.txt", "ours", "shared");

                (await stranger.Client.GetAsync($"/api/v1/objects/{common.Id()}/content", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
                using (var strangerUpload = await stranger.PutObjectAsync(bucketId, "x.txt", ObjectRequests.Bytes("x")))
                {
                    strangerUpload.StatusCode.Should().Be(HttpStatusCode.NotFound);
                }

                using var viewerUpload = await viewer.PutObjectAsync(bucketId, "x.txt", ObjectRequests.Bytes("x"));
                viewerUpload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }
        }

        [Fact]
        public async Task Own_private_object_wins_over_the_shared_one_with_the_same_key()
        {
            var (owner, member, viewer, bucketId) = await SetUpAsync();
            using (owner)
            using (member)
            using (viewer)
            {
                await owner.UploadAsync(bucketId, "notes.txt", "shared copy", "shared");
                await member.UploadAsync(bucketId, "notes.txt", "my copy", "private");
                var url = $"/api/v1/buckets/{bucketId}/objects/content?key=notes.txt";

                (await member.DownloadTextAsync(url)).Should().Be("my copy");
                (await member.DownloadTextAsync(url + "&visibility=shared")).Should().Be("shared copy");
                (await viewer.DownloadTextAsync(url)).Should().Be("shared copy");
            }
        }

        [Fact]
        public async Task Invalid_key_is_rejected()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();

            using var response = await owner.PutObjectAsync(bucketId, "../escape.txt", ObjectRequests.Bytes("x"));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("type").GetString().Should().EndWith("/errors/invalid-key");
        }

        [Fact]
        public async Task Same_key_without_precondition_is_idempotent_for_same_content_and_409_otherwise()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var first = await owner.UploadAsync(bucketId, "a.txt", "v1");

            using var same = await owner.PutObjectAsync(bucketId, "a.txt", ObjectRequests.Bytes("v1"));
            same.StatusCode.Should().Be(HttpStatusCode.OK);
            (await same.Content.ReadFromJsonAsync<JsonElement>(Ct)).Id().Should().Be(first.Id());

            using var different = await owner.PutObjectAsync(bucketId, "a.txt", ObjectRequests.Bytes("v2"));
            different.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await owner.UsedBytesAsync(bucketId)).Should().Be(2);
        }

        [Fact]
        public async Task Preconditions_follow_the_FR_O6_table()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();

            using (var ifMatchOnFreeKey = await owner.PutObjectAsync(bucketId, "a.txt", ObjectRequests.Bytes("v1"), configure: r => r.IfMatch("*")))
            {
                ifMatchOnFreeKey.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
            }

            using (var create = await owner.PutObjectAsync(bucketId, "a.txt", ObjectRequests.Bytes("v1"), configure: r => r.IfNoneMatch("*")))
            {
                create.StatusCode.Should().Be(HttpStatusCode.Created);
            }

            var v1 = await owner.Client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/objects/{(await owner.UploadAsync(bucketId, "a.txt", "v1")).Id()}", Ct);

            using (var createAgain = await owner.PutObjectAsync(bucketId, "a.txt", ObjectRequests.Bytes("v1"), configure: r => r.IfNoneMatch("*")))
            {
                createAgain.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
            }

            using (var wrongTag = await owner.PutObjectAsync(bucketId, "a.txt", ObjectRequests.Bytes("v2"), configure: r => r.IfMatch($"\"{new string('0', 64)}\"")))
            {
                wrongTag.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
            }

            using var overwrite = await owner.PutObjectAsync(
                bucketId, "a.txt", ObjectRequests.Bytes("v2!"), configure: r => r.IfMatch($"\"{v1.GetProperty("sha256").GetString()}\""));
            overwrite.StatusCode.Should().Be(HttpStatusCode.OK);
            var v2 = await overwrite.Content.ReadFromJsonAsync<JsonElement>(Ct);
            v2.Id().Should().NotBe(v1.Id());

            (await owner.Client.GetAsync($"/api/v1/objects/{v1.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.DownloadTextAsync($"/api/v1/buckets/{bucketId}/objects/content?key=a.txt")).Should().Be("v2!");
            (await owner.UsedBytesAsync(bucketId)).Should().Be(3);
        }

        [Fact]
        public async Task Overwriting_another_users_shared_object_needs_bucket_admin()
        {
            var (owner, member, viewer, bucketId) = await SetUpAsync();
            using var admin = await factory.CreateUserAsync();
            await owner.AddMemberAsync(bucketId, admin, "admin");
            using (owner)
            using (member)
            using (viewer)
            {
                await owner.UploadAsync(bucketId, "team.txt", "by owner", "shared");

                using (var byMember = await member.PutObjectAsync(bucketId, "team.txt", ObjectRequests.Bytes("by member"), "shared", configure: r => r.IfMatch("*")))
                {
                    byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                }

                using var byAdmin = await admin.PutObjectAsync(bucketId, "team.txt", ObjectRequests.Bytes("by admin"), "shared", configure: r => r.IfMatch("*"));
                byAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
                (await viewer.DownloadTextAsync($"/api/v1/buckets/{bucketId}/objects/content?key=team.txt")).Should().Be("by admin");
            }
        }

        [Fact]
        public async Task Visibility_only_goes_from_private_to_shared_and_only_by_the_object_owner()
        {
            var (owner, member, viewer, bucketId) = await SetUpAsync();
            using (owner)
            using (member)
            using (viewer)
            {
                var draft = await member.UploadAsync(bucketId, "draft.txt", "draft", "private");
                (await viewer.Client.GetAsync($"/api/v1/objects/{draft.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

                using (var shared = await member.Client.PatchAsJsonAsync($"/api/v1/objects/{draft.Id()}", new { visibility = "shared" }, Ct))
                {
                    shared.StatusCode.Should().Be(HttpStatusCode.OK);
                }

                (await viewer.DownloadTextAsync($"/api/v1/objects/{draft.Id()}/content")).Should().Be("draft");
                (await member.Client.PatchAsJsonAsync($"/api/v1/objects/{draft.Id()}", new { visibility = "private" }, Ct))
                    .StatusCode.Should().Be(HttpStatusCode.Conflict);
                (await owner.Client.PatchAsJsonAsync($"/api/v1/objects/{draft.Id()}", new { visibility = "shared" }, Ct))
                    .StatusCode.Should().Be(HttpStatusCode.Forbidden);

                await owner.UploadAsync(bucketId, "taken.txt", "shared one", "shared");
                var mine = await member.UploadAsync(bucketId, "taken.txt", "private one", "private");
                (await member.Client.PatchAsJsonAsync($"/api/v1/objects/{mine.Id()}", new { visibility = "shared" }, Ct))
                    .StatusCode.Should().Be(HttpStatusCode.Conflict);
            }
        }

        [Fact]
        public async Task Delete_rights_follow_the_matrix()
        {
            var (owner, member, viewer, bucketId) = await SetUpAsync();
            using (owner)
            using (member)
            using (viewer)
            {
                var byOwner = await owner.UploadAsync(bucketId, "o.txt", "owner", "shared");
                var byMember = await member.UploadAsync(bucketId, "m.txt", "member", "shared");

                (await member.Client.DeleteAsync($"/api/v1/objects/{byOwner.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
                (await viewer.Client.DeleteAsync($"/api/v1/objects/{byMember.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
                (await owner.Client.DeleteAsync($"/api/v1/objects/{byMember.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
                (await member.Client.GetAsync($"/api/v1/objects/{byMember.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
                (await owner.Client.DeleteAsync($"/api/v1/objects/{byMember.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            }
        }

        // Owner (bucket-creator) + member + viewer in a new bucket.
        private async Task<(TestUser Owner, TestUser Member, TestUser Viewer, Guid BucketId)> SetUpAsync()
        {
            var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var member = await factory.CreateUserAsync();
            var viewer = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, member, "member");
            await owner.AddMemberAsync(bucketId, viewer, "viewer");
            return (owner, member, viewer, bucketId);
        }
    }
}
