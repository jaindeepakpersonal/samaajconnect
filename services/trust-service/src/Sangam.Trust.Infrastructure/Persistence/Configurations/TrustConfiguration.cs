using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sangam.Trust.Infrastructure.Persistence.Configurations;

// Trust is referenced as `Domain.Trusts.Trust`, never bare - see
// ITrustRepository's own remarks.

public sealed class TrustConfiguration : IEntityTypeConfiguration<Domain.Trusts.Trust>
{
    public void Configure(EntityTypeBuilder<Domain.Trusts.Trust> builder)
    {
        builder.ToTable("trusts");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

        // -----------------------------------------------------------------
        // Exactly one Trust per (Samaaj, Locality). This is the guarantee -
        // not the "does one already exist" read in
        // CreateTrustCommandHandler, which two concurrent requests both pass.
        // -----------------------------------------------------------------
        builder.HasIndex(t => new { t.TenantId, t.LocalityId }).IsUnique();

        builder.Ignore(t => t.DomainEvents);
    }
}
