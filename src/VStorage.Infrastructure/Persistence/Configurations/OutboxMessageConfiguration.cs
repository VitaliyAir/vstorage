using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace VStorage.Infrastructure.Persistence.Configurations
{
    internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).UseIdentityByDefaultColumn();
            builder.Property(m => m.Type).IsRequired();
            builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();

            // The dispatcher reads unprocessed messages in id order.
            builder.HasIndex(m => m.Id).HasDatabaseName("ix_outbox_pending").HasFilter("processed_at IS NULL");
        }
    }
}
