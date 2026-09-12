using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Matrimony.Domain.Biodata;

namespace Sangam.Matrimony.Infrastructure.Persistence.Configurations;

public sealed class BiodataProfileConfiguration : IEntityTypeConfiguration<BiodataProfile>
{
    public void Configure(EntityTypeBuilder<BiodataProfile> builder)
    {
        builder.ToTable("biodata_profiles");

        // The key is the member's own id, domain-assigned - never generated
        // here. This repo has hit the ValueGeneratedOnAdd default three
        // times already on a domain-assigned key; this is the fourth place
        // it must not.
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.FullName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Gender).HasConversion<string>().HasMaxLength(20).IsRequired();

        // No max length on the Guid ids: GautraId/ProfessionId/QualificationId/
        // CityId/LocalityId point at master-data-service or member-family-service,
        // held as plain foreign keys with no referential integrity across the
        // service boundary - the same stance every service holding one of
        // these ids already takes.
        builder.Property(p => p.Diet).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.FamilyType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.FatherOccupation).HasMaxLength(120);
        builder.Property(p => p.MotherOccupation).HasMaxLength(120);
        builder.Property(p => p.HoroscopeNotes).HasMaxLength(500);
        builder.Property(p => p.AboutMe).HasMaxLength(1000);
        builder.Property(p => p.PartnerPreferenceNotes).HasMaxLength(500);

        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();

        builder.OwnsOne(p => p.Consent, consent =>
        {
            consent.Property(c => c.NoticeVersion).HasColumnName("consent_notice_version")
                .HasMaxLength(20).IsRequired();
            consent.Property(c => c.Attestation).HasColumnName("consent_attestation").IsRequired();
            consent.Property(c => c.GivenAt).HasColumnName("consent_given_at").IsRequired();
            consent.Property(c => c.WithdrawnAt).HasColumnName("consent_withdrawn_at");
        });

        builder.Navigation(p => p.Consent).IsRequired();

        // The search's common case: active profiles in this Samaaj.
        builder.HasIndex(p => new { p.TenantId, p.IsActive });

        builder.Ignore(p => p.DomainEvents);
    }
}
