using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VStorage.Domain.Buckets;
using VStorage.Domain.Principals;

namespace VStorage.Infrastructure.Persistence.Configurations
{
    internal sealed class BucketConfiguration : IEntityTypeConfiguration<Bucket>
    {
        public void Configure(EntityTypeBuilder<Bucket> builder)
        {
            builder.ToTable("buckets", table => table.HasCheckConstraint("ck_buckets_quota_bytes", "quota_bytes IS NULL OR quota_bytes > 0"));

            builder.HasKey(b => b.Id);
            builder.Property(b => b.Id).ValueGeneratedNever();
            builder.Property(b => b.Name)
                .HasConversion(name => name.Value, value => BucketName.FromStorage(value))
                .IsRequired();

            builder.HasOne<Principal>().WithMany().HasForeignKey(b => b.OwnerId).OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(b => new { b.OwnerId, b.Name })
                .IsUnique()
                .HasDatabaseName("ux_buckets_owner_name")
                .HasFilter("deleted_at IS NULL");

            // PostgreSQL xmin as optimistic concurrency token.
            builder.Property(b => b.Version).IsRowVersion();

            builder.HasMany(b => b.Members).WithOne().HasForeignKey(m => m.BucketId).OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(b => b.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }

    internal sealed class BucketMemberConfiguration : IEntityTypeConfiguration<BucketMember>
    {
        public void Configure(EntityTypeBuilder<BucketMember> builder)
        {
            builder.ToTable("bucket_members", table => table.HasCheckConstraint("ck_bucket_members_role", "role IN ('admin','member','viewer')"));

            builder.HasKey(m => new { m.BucketId, m.UserId });
            builder.Property(m => m.Role).HasConversion(EnumText.Converter<BucketRole>()).IsRequired();

            builder.HasOne<Principal>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<Principal>().WithMany().HasForeignKey(m => m.AddedBy).OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(m => m.UserId);
        }
    }
}
