using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VStorage.Domain.Buckets;
using VStorage.Domain.Objects;
using VStorage.Domain.Principals;
using VStorage.Infrastructure.Persistence;

namespace VStorage.Api.IntegrationTests.Persistence
{
    /// <summary>Checks the migrated schema: constraints, partial unique indexes, outbox writes, role mapping.</summary>
    [Collection(ApiCollection.Name)]
    public sealed class SchemaTests(ApiFactory factory)
    {
        private static readonly string Sha = new('a', 64);

        private CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task CreatingBucket_WritesBucketCreatedToOutbox()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VStorageDbContext>();
            var owner = await AddPrincipalAsync(db);

            var bucket = NewBucket(owner.Id);
            db.Buckets.Add(bucket);
            await db.SaveChangesAsync(Ct);

            var messages = await db.OutboxMessages.Where(m => m.Type == "BucketCreated").ToListAsync(Ct);
            var message = messages.Single(m => m.Payload.Contains(bucket.Id.ToString()));
            using var payload = JsonDocument.Parse(message.Payload);
            payload.RootElement.GetProperty("data").GetProperty("ownerId").GetGuid().Should().Be(owner.Id);
            message.ProcessedAt.Should().BeNull();
        }

        [Fact]
        public async Task SharedKey_IsUniquePerBucket_ButPrivateCopyWithSameKeyIsAllowed()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VStorageDbContext>();
            var owner = await AddPrincipalAsync(db);
            var bucket = NewBucket(owner.Id);
            db.Buckets.Add(bucket);
            await db.SaveChangesAsync(Ct);

            db.Objects.Add(NewAvailableObject(bucket.Id, owner.Id, "docs/a.pdf", Visibility.Shared));
            db.Objects.Add(NewAvailableObject(bucket.Id, owner.Id, "docs/a.pdf", Visibility.Private));
            await db.SaveChangesAsync(Ct);

            db.Objects.Add(NewAvailableObject(bucket.Id, owner.Id, "docs/a.pdf", Visibility.Shared));
            var save = () => db.SaveChangesAsync(Ct);

            (await save.Should().ThrowAsync<DbUpdateException>())
                .Which.InnerException!.Message.Should().Contain("ux_objects_shared_key");
        }

        [Fact]
        public async Task OwnershipTransfer_IsPersisted_WithPreviousOwnerAsAdmin()
        {
            Guid bucketId;
            Principal owner;
            Principal member;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VStorageDbContext>();
                owner = await AddPrincipalAsync(db);
                member = await AddPrincipalAsync(db);
                var bucket = NewBucket(owner.Id);
                bucket.AddMember(member.Id, BucketRole.Member, owner.Id, DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
                db.Buckets.Add(bucket);
                await db.SaveChangesAsync(Ct);
                bucketId = bucket.Id;
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VStorageDbContext>();
                var bucket = await db.Buckets.Include(b => b.Members).SingleAsync(b => b.Id == bucketId, Ct);
                bucket.TransferOwnership(member.Id, requireMembership: true, DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
                await db.SaveChangesAsync(Ct);
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VStorageDbContext>();
                var bucket = await db.Buckets.Include(b => b.Members).SingleAsync(b => b.Id == bucketId, Ct);
                bucket.OwnerId.Should().Be(member.Id);
                bucket.RoleOf(owner.Id).Should().Be(BucketRole.Admin);
                bucket.Members.Should().ContainSingle();
            }
        }

        private static async Task<Principal> AddPrincipalAsync(VStorageDbContext db)
        {
            var principal = Principal.Create(Guid.NewGuid().ToString(), PrincipalKind.User, null, false, "Test user", DateTimeOffset.UtcNow);
            db.Principals.Add(principal);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return principal;
        }

        private static Bucket NewBucket(Guid ownerId) =>
            Bucket.Create(BucketName.Create($"b-{Guid.NewGuid():N}"[..20]).Value, ownerId, null, DateTimeOffset.UtcNow).Value;

        private static StoredObject NewAvailableObject(Guid bucketId, Guid ownerId, string key, Visibility visibility)
        {
            var now = DateTimeOffset.UtcNow;
            var storedObject = StoredObject.BeginUpload(bucketId, ObjectKey.Create(key).Value, ownerId, visibility, "application/pdf", "a.pdf", now).Value;
            storedObject.Complete(10, Sha, now).IsSuccess.Should().BeTrue();
            return storedObject;
        }
    }
}
