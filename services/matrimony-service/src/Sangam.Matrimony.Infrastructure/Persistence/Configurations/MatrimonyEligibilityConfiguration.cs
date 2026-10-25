using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Matrimony.Domain.Biodata;

namespace Sangam.Matrimony.Infrastructure.Persistence.Configurations;

public sealed class MatrimonyEligibilityConfiguration : IEntityTypeConfiguration<MatrimonyEligibility>
{
    public void Configure(EntityTypeBuilder<MatrimonyEligibility> builder)
    {
        builder.ToTable("matrimony_eligibility");

        // The member's own id, assigned by member-family-service - never
        // generated here - within their Samaaj.
        builder.HasKey(e => new { e.TenantId, e.MemberId });
        builder.Property(e => e.MemberId).ValueGeneratedNever();
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.IsEligible).IsRequired();
        builder.Property(e => e.ChangedAt).IsRequired();
    }
}
