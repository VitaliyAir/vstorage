using Microsoft.EntityFrameworkCore;
using VStorage.Application.Abstractions;
using VStorage.Domain.Buckets;
using VStorage.Domain.Common;
using VStorage.Domain.Objects;
using VStorage.Domain.Principals;
using VStorage.Domain.Sharing;

namespace VStorage.Infrastructure.Persistence
{
    public sealed class VStorageDbContext(DbContextOptions<VStorageDbContext> options) : DbContext(options), IVStorageDbContext
    {
        public DbSet<Principal> Principals => Set<Principal>();

        public DbSet<Bucket> Buckets => Set<Bucket>();

        public DbSet<StoredObject> Objects => Set<StoredObject>();

        public DbSet<ShareLink> ShareLinks => Set<ShareLink>();

        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(VStorageDbContext).Assembly);

            // Domain events are not columns; the OutboxInterceptor turns them into outbox_messages rows.
            foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
            {
                modelBuilder.Entity(entityType.ClrType).Ignore(nameof(Entity.DomainEvents));
            }
        }
    }
}
