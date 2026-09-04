using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.IdentityTenant.Domain.Media;

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Configurations;

public sealed class TenantLogoConfiguration : IEntityTypeConfiguration<TenantLogo>
{
    public void Configure(EntityTypeBuilder<TenantLogo> builder)
    {
        builder.ToTable("tenant_logos");

        builder.HasKey(l => l.Id);

        // Domain-assigned. Left as EF's default ValueGeneratedOnAdd, a logo
        // added to a tracked graph comes back Modified rather than Added and the
        // save fails against a row that was never there.
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.ContentType).HasMaxLength(32).IsRequired();
        builder.Property(l => l.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(l => l.ByteSize).IsRequired();
        builder.Property(l => l.UploadedBy).IsRequired();
        builder.Property(l => l.UploadedAt).IsRequired();

        // Postgres TOASTs anything over ~2KB out of the main heap and compresses
        // it, so the table stays small and a query that does not select this
        // column does not read it.
        builder.Property(l => l.Bytes).IsRequired();

        // How archiving a Samaaj finds every logo it has, including any a
        // replace path orphaned. Not unique: a Samaaj may briefly have two rows
        // while a logo is replaced inside one transaction.
        builder.HasIndex(l => l.TenantId).HasDatabaseName("ix_tenant_logos_tenant");
    }
}
