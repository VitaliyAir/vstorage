using System.Net.Http.Json;
using System.Text.Json;

namespace VStorage.Api.IntegrationTests
{
    /// <summary>A provisioned principal (GET /me done) with an authenticated client.</summary>
    public sealed class TestUser : IDisposable
    {
        public required Guid Id { get; init; }

        public required HttpClient Client { get; init; }

        public void Dispose() => Client.Dispose();
    }

    public static class TestUsers
    {
        public const string BucketCreator = "bucket-creator";
        public const string PlatformAdmin = "vstorage-admin";

        /// <summary>Creates a user (new random sub) with optional vstorage client roles and provisions it.</summary>
        public static Task<TestUser> CreateUserAsync(this ApiFactory factory, string? email = null, params string[] vstorageRoles) =>
            factory.SignInAsync(TestTokens.Create(
                Guid.NewGuid().ToString(),
                email: email,
                emailVerified: email is not null,
                name: email ?? "Test user",
                clientRoles: Roles(vstorageRoles)));

        /// <summary>Creates a Keycloak-style service account of <paramref name="clientId"/> and provisions it.</summary>
        public static Task<TestUser> CreateServiceAccountAsync(this ApiFactory factory, string clientId, params string[] vstorageRoles) =>
            factory.SignInAsync(TestTokens.ServiceAccount(clientId, Guid.NewGuid().ToString(), Roles(vstorageRoles)));

        /// <summary>A unique valid bucket name ("b-" + 18 hex characters).</summary>
        public static string NewBucketName() => $"b-{Guid.NewGuid():N}"[..20];

        /// <summary>Creates a bucket through POST /api/v1/buckets (the user needs bucket-creator) and returns its id.</summary>
        public static async Task<Guid> CreateBucketAsync(this TestUser owner, string? name = null)
        {
            using var response = await owner.Client.PostAsJsonAsync(
                "/api/v1/buckets", new { name = name ?? NewBucketName() }, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            var bucket = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            return bucket.GetProperty("id").GetGuid();
        }

        /// <summary>Adds <paramref name="user"/> with <paramref name="role"/> through the owner's/admin's client.</summary>
        public static async Task AddMemberAsync(this TestUser manager, Guid bucketId, TestUser user, string role)
        {
            using var response = await manager.Client.PostAsJsonAsync(
                $"/api/v1/buckets/{bucketId}/members", new { userId = user.Id, role }, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
        }

        private static async Task<TestUser> SignInAsync(this ApiFactory factory, string token)
        {
            var client = factory.CreateClient(token);
            var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me", TestContext.Current.CancellationToken);
            return new TestUser { Id = me.GetProperty("id").GetGuid(), Client = client };
        }

        private static Dictionary<string, string[]>? Roles(string[] vstorageRoles) =>
            vstorageRoles.Length == 0 ? null : new Dictionary<string, string[]> { ["vstorage"] = vstorageRoles };
    }
}
