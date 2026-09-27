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

        // A Trust's roles, loaded with it. Trustee is one of them - see TrustRole.
        builder.OwnsMany(t => t.Roles, role =>
        {
            role.ToTable("trust_roles");
            role.WithOwner().HasForeignKey(r => r.TrustId);
            role.HasKey(r => r.Id);
            role.Property(r => r.Id).ValueGeneratedNever();
            role.Property(r => r.Title).IsRequired().HasMaxLength(100);
            role.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        });

        builder.Metadata.FindNavigation(nameof(Domain.Trusts.Trust.Roles))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(t => t.DomainEvents);
    }
}
