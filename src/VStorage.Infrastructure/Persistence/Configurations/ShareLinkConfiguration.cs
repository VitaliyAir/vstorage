using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VStorage.Domain.Objects;
using VStorage.Domain.Principals;
using VStorage.Domain.Sharing;

namespace VStorage.Infrastructure.Persistence.Configurations
{
    internal sealed class ShareLinkConfiguration : IEntityTypeConfiguration<ShareLink>
    {
        public void Configure(EntityTypeBuilder<ShareLink> builder)
        {
            builder.ToTable("share_links", table =>
            {
                table.HasCheckConstraint("ck_share_links_kind", "kind IN ('public','addressed')");
                table.HasCheckConstraint("ck_share_links_max_downloads", "max_downloads IS NULL OR max_downloads > 0");
            });

            builder.HasKey(l => l.Id);
            builder.Property(l => l.Id).ValueGeneratedNever();
            builder.Property(l => l.Slug).IsRequired();
            builder.HasIndex(l => l.Slug).IsUnique();
            builder.Property(l => l.Kind).HasConversion(EnumText.Converter<ShareLinkKind>()).IsRequired();

            builder.HasOne<StoredObject>().WithMany().HasForeignKey(l => l.ObjectId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<Principal>().WithMany().HasForeignKey(l => l.CreatedBy).OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(l => l.Recipients).WithOne().HasForeignKey(r => r.ShareLinkId).OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(l => l.Recipients).UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }

    internal sealed class ShareLinkRecipientConfiguration : IEntityTypeConfiguration<ShareLinkRecipient>
    {
        public void Configure(EntityTypeBuilder<ShareLinkRecipient> builder)
        {
            builder.ToTable("share_link_recipients");
            builder.HasKey(r => new { r.ShareLinkId, r.UserId });
            builder.HasOne<Principal>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(r => r.UserId);
        }
    }
}
