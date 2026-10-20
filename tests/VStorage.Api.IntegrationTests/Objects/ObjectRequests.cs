using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VStorage.Api.IntegrationTests.Objects
{
    /// <summary>Helpers for object uploads and downloads in tests.</summary>
    public static class ObjectRequests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

        public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

        /// <summary>PUT /buckets/{id}/objects?key=&amp;visibility= with a raw body.</summary>
        public static Task<HttpResponseMessage> PutObjectAsync(
            this TestUser user,
            Guid bucketId,
            string key,
            byte[] content,
            string visibility = "private",
            string contentType = "application/octet-stream",
            string? fileName = null,
            Action<HttpRequestMessage>? configure = null)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Put, $"/api/v1/buckets/{bucketId}/objects?key={Uri.EscapeDataString(key)}&visibility={visibility}")
            {
                Content = new ByteArrayContent(content)
            };
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            if (fileName is not null)
            {
                request.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName };
            }

            configure?.Invoke(request);
            return user.Client.SendAsync(request, Ct);
        }

        /// <summary>Uploads and returns the object JSON (fails the test on a non-success status).</summary>
        public static async Task<JsonElement> UploadAsync(
            this TestUser user, Guid bucketId, string key, string text, string visibility = "private", string contentType = "text/plain")
        {
            using var response = await user.PutObjectAsync(bucketId, key, Bytes(text), visibility, contentType);
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }

        public static async Task<string> DownloadTextAsync(this TestUser user, string url)
        {
            using var response = await user.Client.GetAsync(url, Ct);
            response.IsSuccessStatusCode.Should().BeTrue($"GET {url} returned {(int)response.StatusCode}");
            return await response.Content.ReadAsStringAsync(Ct);
        }

        /// <summary>GET /buckets/{id}/objects?{query}; fails the test on a non-success status.</summary>
        public static async Task<JsonElement> ListAsync(this TestUser user, Guid bucketId, string query = "")
        {
            using var response = await user.Client.GetAsync($"/api/v1/buckets/{bucketId}/objects?{query}", Ct);
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }

        public static void IfMatch(this HttpRequestMessage request, string value) => request.Headers.TryAddWithoutValidation("If-Match", value);

        public static void IfNoneMatch(this HttpRequestMessage request, string value) => request.Headers.TryAddWithoutValidation("If-None-Match", value);

        public static Guid Id(this JsonElement json) => json.GetProperty("id").GetGuid();

        public static async Task<long> UsedBytesAsync(this TestUser owner, Guid bucketId) =>
            (await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/buckets/{bucketId}", Ct)).GetProperty("usedBytes").GetInt64();
    }
}
