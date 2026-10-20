using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace VStorage.Api.IntegrationTests.Objects
{
    /// <summary>Upload and download: streaming, hashes, Range, conditional GET, headers, limits (spec §5.6, §7, SEC-6).</summary>
    [Collection(ApiCollection.Name)]
    public sealed class ObjectTransferTests(ApiFactory factory)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Upload_and_download_round_trip_with_hash_and_safe_headers()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var content = ObjectRequests.Bytes("hello, vstorage");

            using var put = await owner.PutObjectAsync(bucketId, "docs/hello.txt", content, contentType: "text/plain", fileName: "Привет.txt");

            put.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await put.Content.ReadFromJsonAsync<JsonElement>(Ct);
            var sha = ObjectRequests.Sha256(content);
            created.GetProperty("sha256").GetString().Should().Be(sha);
            created.GetProperty("size").GetInt64().Should().Be(content.Length);
            created.GetProperty("visibility").GetString().Should().Be("private");
            put.Headers.ETag!.Tag.Should().Be($"\"{sha}\"");
            put.Headers.Location!.ToString().Should().Be($"/api/v1/objects/{created.Id()}");

            using var get = await owner.Client.GetAsync($"/api/v1/objects/{created.Id()}/content", Ct);
            get.StatusCode.Should().Be(HttpStatusCode.OK);
            (await get.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(content);
            get.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
            get.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
            get.Content.Headers.ContentDisposition.FileNameStar.Should().Be("Привет.txt");
            get.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
            get.Headers.GetValues("Content-Security-Policy").Should().Equal("sandbox");
            get.Headers.AcceptRanges.Should().Contain("bytes");

            (await owner.DownloadTextAsync($"/api/v1/buckets/{bucketId}/objects/content?key=docs/hello.txt")).Should().Be("hello, vstorage");

            using var head = await owner.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/api/v1/objects/{created.Id()}/content"), Ct);
            head.StatusCode.Should().Be(HttpStatusCode.OK);
            head.Content.Headers.ContentLength.Should().Be(content.Length);
            (await head.Content.ReadAsByteArrayAsync(Ct)).Should().BeEmpty();
        }

        [Fact]
        public async Task Range_and_conditional_requests_are_supported()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var created = await owner.UploadAsync(bucketId, "range.txt", "0123456789");
            var url = $"/api/v1/objects/{created.Id()}/content";

            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, url);
            rangeRequest.Headers.Range = new RangeHeaderValue(2, 5);
            using var range = await owner.Client.SendAsync(rangeRequest, Ct);
            range.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            (await range.Content.ReadAsStringAsync(Ct)).Should().Be("2345");

            using var conditional = new HttpRequestMessage(HttpMethod.Get, url);
            conditional.IfNoneMatch($"\"{created.GetProperty("sha256").GetString()}\"");
            using var notModified = await owner.Client.SendAsync(conditional, Ct);
            notModified.StatusCode.Should().Be(HttpStatusCode.NotModified);
        }

        [Fact]
        public async Task Multipart_upload_keeps_the_file_name_and_pdf_may_be_inline()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            using var form = new MultipartFormDataContent
            {
                { new StringContent("reports/2026.pdf"), "key" },
                { new StringContent("shared"), "visibility" }
            };
            var file = new ByteArrayContent(ObjectRequests.Bytes("%PDF-1.7 fake"));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "Report 2026.pdf");

            using var response = await owner.Client.PostAsync($"/api/v1/buckets/{bucketId}/objects", form, Ct);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            created.GetProperty("originalFileName").GetString().Should().Be("Report 2026.pdf");
            created.GetProperty("contentType").GetString().Should().Be("application/pdf");
            created.GetProperty("visibility").GetString().Should().Be("shared");

            using var inline = await owner.Client.GetAsync($"/api/v1/objects/{created.Id()}/content?disposition=inline", Ct);
            inline.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        }

        [Fact]
        public async Task Multipart_fields_must_precede_the_file()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(ObjectRequests.Bytes("x")), "file", "x.bin");
            form.Add(new StringContent("x.bin"), "key");

            using var response = await owner.Client.PostAsync($"/api/v1/buckets/{bucketId}/objects", form, Ct);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Html_is_always_an_attachment()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var created = await owner.UploadAsync(bucketId, "page.html", "<script>alert(1)</script>", contentType: "text/html");

            using var response = await owner.Client.GetAsync($"/api/v1/objects/{created.Id()}/content?disposition=inline", Ct);

            response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
            response.Headers.GetValues("Content-Security-Policy").Should().Equal("sandbox");
        }

        [Fact]
        public async Task Too_large_uploads_are_rejected_with_and_without_content_length()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var tooLarge = new byte[ApiFactory.MaxUploadBytes + 1];

            using var declared = await owner.PutObjectAsync(bucketId, "big.bin", tooLarge);
            declared.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

            using var chunked = await owner.PutObjectAsync(bucketId, "big.bin", tooLarge, configure: request =>
            {
                request.Headers.TransferEncodingChunked = true;
                request.Content!.Headers.ContentLength = null;
            });
            chunked.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

            (await owner.Client.GetAsync($"/api/v1/buckets/{bucketId}/objects/content?key=big.bin", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.UsedBytesAsync(bucketId)).Should().Be(0);
        }

        [Fact]
        public async Task Quota_is_enforced_and_released_on_delete()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            (await owner.Client.PatchAsJsonAsync($"/api/v1/buckets/{bucketId}", new { quotaBytes = 10 }, Ct)).EnsureSuccessStatusCode();

            var first = await owner.UploadAsync(bucketId, "a.txt", "12345678");
            (await owner.UsedBytesAsync(bucketId)).Should().Be(8);

            using var over = await owner.PutObjectAsync(bucketId, "b.txt", ObjectRequests.Bytes("12345"));
            over.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
            (await over.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("type").GetString().Should().EndWith("/errors/quota-exceeded");

            (await owner.Client.DeleteAsync($"/api/v1/objects/{first.Id()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await owner.UsedBytesAsync(bucketId)).Should().Be(0);
        }

        [Fact]
        public async Task Blob_is_stored_under_the_id_based_path()
        {
            using var owner = await factory.CreateUserAsync(vstorageRoles: TestUsers.BucketCreator);
            var bucketId = await owner.CreateBucketAsync();
            var created = await owner.UploadAsync(bucketId, "keys/never/become/paths.txt", "on disk");
            var id = created.Id().ToString("N");

            var path = Path.Combine(factory.BlobRoot, "objects", bucketId.ToString("N"), id[^2..], id[^4..^2], id);

            File.Exists(path).Should().BeTrue();
            (await File.ReadAllTextAsync(path, Ct)).Should().Be("on disk");
            Directory.EnumerateFiles(Path.Combine(factory.BlobRoot, "tmp")).Should().NotContain(f => f.EndsWith(id));
        }
    }
}
