using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VStorage.Domain.Buckets;
using VStorage.Domain.Objects;
using VStorage.Domain.Principals;

namespace VStorage.Infrastructure.Persistence.Configurations
{
    internal sealed class StoredObjectConfiguration : IEntityTypeConfiguration<StoredObject>
    {
        public void Configure(EntityTypeBuilder<StoredObject> builder)
        {
            builder.ToTable("objects", table =>
            {
                table.HasCheckConstraint("ck_objects_visibility", "visibility IN ('shared','private')");
                table.HasCheckConstraint("ck_objects_status", "status IN ('pending','available','deleted')");
                table.HasCheckConstraint("ck_objects_available_has_content", "status <> 'available' OR (size IS NOT NULL AND sha256 IS NOT NULL)");
            });

            builder.HasKey(o => o.Id);
            builder.Property(o => o.Id).ValueGeneratedNever();

            // Byte-order collation: prefix listing and cursors behave like S3 (no locale rules).
            builder.Property(o => o.Key)
                .HasConversion(key => key.Value, value => ObjectKey.FromStorage(value))
                .UseCollation("C")
                .IsRequired();

            builder.Property(o => o.Visibility).HasConversion(EnumText.Converter<Visibility>()).IsRequired();
            builder.Property(o => o.Status).HasConversion(EnumText.Converter<ObjectStatus>()).IsRequired();
            builder.Property(o => o.Sha256).HasColumnType("char(64)");
            builder.Property(o => o.ContentType).IsRequired();

            builder.HasOne<Bucket>().WithMany().HasForeignKey(o => o.BucketId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<Principal>().WithMany().HasForeignKey(o => o.OwnerId).OnDelete(DeleteBehavior.Restrict);

            // One available shared object per key; one available private object per (owner, key).
            builder.HasIndex(o => new { o.BucketId, o.Key })
                .IsUnique()
                .HasDatabaseName("ux_objects_shared_key")
                .HasFilter("visibility = 'shared' AND status = 'available'");
            builder.HasIndex(o => new { o.BucketId, o.OwnerId, o.Key })
                .IsUnique()
                .HasDatabaseName("ux_objects_private_key")
                .HasFilter("visibility = 'private' AND status = 'available'");

            builder.HasIndex(o => new { o.BucketId, o.Key, o.Id })
                .HasDatabaseName("ix_objects_listing")
                .HasFilter("status = 'available'");
            builder.HasIndex(o => new { o.Status, o.UpdatedAt })
                .HasDatabaseName("ix_objects_cleanup")
                .HasFilter("status <> 'available'");
        }
    }
}
