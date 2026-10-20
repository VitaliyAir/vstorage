using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VStorage.Domain.Principals;

namespace VStorage.Infrastructure.Persistence.Configurations
{
    internal sealed class PrincipalConfiguration : IEntityTypeConfiguration<Principal>
    {
        public void Configure(EntityTypeBuilder<Principal> builder)
        {
            builder.ToTable("users", table => table.HasCheckConstraint("ck_users_kind", "kind IN ('user','service')"));

            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.Property(p => p.KeycloakSub).IsRequired();
            builder.HasIndex(p => p.KeycloakSub).IsUnique();
            builder.Property(p => p.Kind).HasConversion(EnumText.Converter<PrincipalKind>()).IsRequired();

            // Emails are stored lower-case, so a plain index serves the exact /users/lookup query.
            builder.HasIndex(p => p.Email).HasDatabaseName("ix_users_email").HasFilter("email_verified");
        }
    }
}
