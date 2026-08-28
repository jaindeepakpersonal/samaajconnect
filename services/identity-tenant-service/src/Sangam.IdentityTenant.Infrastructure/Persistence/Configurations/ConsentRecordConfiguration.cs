using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.IdentityTenant.Domain.Consents;

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Configurations;

public sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("consent_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.UserId).IsRequired();
        builder.Property(r => r.NoticeVersion).HasMaxLength(40).IsRequired();
        builder.Property(r => r.Source).HasMaxLength(60).IsRequired();
        builder.Property(r => r.RecordedAt).IsRequired();

        builder.Property(r => r.Purpose).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(r => r.Action).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Read as "this member's history, oldest first", which is both the
        // export and the way current state is derived.
        builder.HasIndex(r => new { r.UserId, r.RecordedAt });

        builder.Ignore(r => r.DomainEvents);
    }
}
