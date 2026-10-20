using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VStorage.Api.IntegrationTests.Objects
{
    /// <summary>Listing: visibility, byte order, prefix/delimiter, cursor pages, validation and access (spec FR-O11).</summary>
    [Collection(ApiCollection.Name)]
    public sealed class ObjectListingTests(ApiFactory factory)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Lists_shared_and_own_private_objects_in_byte_order()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            using var member = await factory.CreateUserAsync();
            using var viewer = await factory.CreateUserAsync();
            var bucketId = await owner.CreateBucketAsync();
            await owner.AddMemberAsync(bucketId, member, "member");
            await owner.AddMemberAsync(bucketId, viewer, "viewer");

            await owner.UploadAsync(bucketId, "b.txt", "shared b", "shared");
            await owner.UploadAsync(bucketId, "B.txt", "shared B", "shared");
            await owner.UploadAsync(bucketId, "ä.txt", "shared ä", "shared");
            await owner.UploadAsync(bucketId, "z.txt", "owner only", "private");
            await member.UploadAsync(bucketId, "a.txt", "member only", "private");
            await member.UploadAsync(bucketId, "b.txt", "member's own b", "private");

            var forMember = await member.ListAsync(bucketId);
            Keys(forMember).Should().Equal("B.txt", "a.txt", "b.txt", "b.txt", "ä.txt");
            forMember.GetProperty("objects").EnumerateArray()
                .Where(o => o.GetProperty("key").GetString() == "a.txt")
                .Single().GetProperty("visibility").GetString().Should().Be("private");
            forMember.GetProperty("commonPrefixes").GetArrayLength().Should().Be(0);
            forMember.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);

            Keys(await viewer.ListAsync(bucketId)).Should().Equal("B.txt", "b.txt", "ä.txt");
            Keys(await owner.ListAsync(bucketId)).Should().Equal("B.txt", "b.txt", "z.txt", "ä.txt");
        }

        [Fact]
        public async Task Delimiter_rolls_keys_up_into_common_prefixes()
        {
            var (owner, bucketId) = await BucketWithAsync("a.txt", "docs/1.txt", "docs/2.txt", "docs/sub/3.txt", "img/x.png", "z.txt");
            using (owner)
            {
                var top = await owner.ListAsync(bucketId, "delimiter=/");
                Keys(top).Should().Equal("a.txt", "z.txt");
                Prefixes(top).Should().Equal("docs/", "img/");

                var docs = await owner.ListAsync(bucketId, "prefix=docs/&delimiter=/");
                Keys(docs).Should().Equal("docs/1.txt", "docs/2.txt");
                Prefixes(docs).Should().Equal("docs/sub/");

                Keys(await owner.ListAsync(bucketId, "prefix=docs/")).Should().Equal("docs/1.txt", "docs/2.txt", "docs/sub/3.txt");
                Keys(await owner.ListAsync(bucketId, "prefix=do")).Should().HaveCount(3);
            }
        }

        [Fact]
        public async Task Like_wildcards_in_the_prefix_are_literal()
        {
            var (owner, bucketId) = await BucketWithAsync("100%/a", "100x/b", "a_b/c", "axb/d");
            using (owner)
            {
                Keys(await owner.ListAsync(bucketId, $"prefix={Uri.EscapeDataString("100%")}")).Should().Equal("100%/a");
                Keys(await owner.ListAsync(bucketId, "prefix=a_")).Should().Equal("a_b/c");
            }
        }

        [Fact]
        public async Task Cursor_pages_visit_every_entry_once()
        {
            var (owner, bucketId) = await BucketWithAsync(
                "a.txt", "dir1/1", "dir1/2", "dir1/3", "dir1/4", "dir1/5", "dir2/1", "m.txt", "z.txt");
            using (owner)
            {
                var page1 = await owner.ListAsync(bucketId, "delimiter=/&limit=2");
                Keys(page1).Should().Equal("a.txt");
                Prefixes(page1).Should().Equal("dir1/");

                var page2 = await owner.ListAsync(bucketId, $"delimiter=/&limit=2&cursor={NextCursor(page1)}");
                Keys(page2).Should().Equal("m.txt");
                Prefixes(page2).Should().Equal("dir2/");

                var page3 = await owner.ListAsync(bucketId, $"delimiter=/&limit=2&cursor={NextCursor(page2)}");
                Keys(page3).Should().Equal("z.txt");
                Prefixes(page3).Should().BeEmpty();
                page3.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);

                // Exactly `limit` entries left: no empty extra page.
                (await owner.ListAsync(bucketId, "delimiter=/&limit=5")).GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);

                var flat = new List<string>();
                string? cursor = null;
                do
                {
                    var page = await owner.ListAsync(bucketId, cursor is null ? "limit=4" : $"limit=4&cursor={cursor}");
                    flat.AddRange(Keys(page));
                    cursor = page.GetProperty("nextCursor").GetString() is { } next ? Uri.EscapeDataString(next) : null;
                }
                while (cursor is not null);

                flat.Should().Equal("a.txt", "dir1/1", "dir1/2", "dir1/3", "dir1/4", "dir1/5", "dir2/1", "m.txt", "z.txt");
            }
        }

        [Fact]
        public async Task Invalid_parameters_and_foreign_cursors_are_rejected()
        {
            var (owner, bucketId) = await BucketWithAsync("dir1/1", "dir2/1");
            using (owner)
            {
                var cursor = NextCursor(await owner.ListAsync(bucketId, "delimiter=/&limit=1"));

                (await ProblemCodeAsync(owner, bucketId, $"prefix=dir1/&delimiter=/&limit=1&cursor={cursor}")).Should().Be("invalid-cursor");
                (await ProblemCodeAsync(owner, bucketId, "cursor=garbage!")).Should().Be("invalid-cursor");
                (await ProblemCodeAsync(owner, bucketId, "limit=0")).Should().Be("invalid-limit");
                (await ProblemCodeAsync(owner, bucketId, "limit=1001")).Should().Be("invalid-limit");
                (await ProblemCodeAsync(owner, bucketId, "delimiter=//")).Should().Be("invalid-delimiter");
                (await ProblemCodeAsync(owner, bucketId, $"prefix={Uri.EscapeDataString(@"a\b")}")).Should().Be("invalid-prefix");
            }
        }

        [Fact]
        public async Task Non_members_get_404_and_deleted_objects_are_not_listed()
        {
            var (owner, bucketId) = await BucketWithAsync("keep.txt");
            using var stranger = await factory.CreateUserAsync();
            using var platformAdmin = await factory.CreateUserAsync(vstorageRoles: TestUsers.PlatformAdmin);
            using (owner)
            {
                var gone = await owner.UploadAsync(bucketId, "gone.txt", "x");
                (await owner.Client.DeleteAsync($"/api/v1/objects/{gone.Id()}", Ct)).EnsureSuccessStatusCode();

                Keys(await owner.ListAsync(bucketId)).Should().Equal("keep.txt");
                (await stranger.Client.GetAsync($"/api/v1/buckets/{bucketId}/objects", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
                (await platformAdmin.Client.GetAsync($"/api/v1/buckets/{bucketId}/objects", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            }
        }

        private async Task<(TestUser Owner, Guid BucketId)> BucketWithAsync(params string[] keys)
        {
            var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            foreach (var key in keys)
            {
                await owner.UploadAsync(bucketId, key, key);
            }

            return (owner, bucketId);
        }

        private static async Task<string> ProblemCodeAsync(TestUser user, Guid bucketId, string query)
        {
            using var response = await user.Client.GetAsync($"/api/v1/buckets/{bucketId}/objects?{query}", Ct);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var type = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("type").GetString()!;
            return type[(type.LastIndexOf('/') + 1)..];
        }

        private static List<string> Keys(JsonElement listing) =>
            listing.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty("key").GetString()!).ToList();

        private static List<string> Prefixes(JsonElement listing) =>
            listing.GetProperty("commonPrefixes").EnumerateArray().Select(p => p.GetString()!).ToList();

        private static string NextCursor(JsonElement listing) => Uri.EscapeDataString(listing.GetProperty("nextCursor").GetString()!);
    }
}
