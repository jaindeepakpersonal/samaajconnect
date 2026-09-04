using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.MemberFamily.Domain.Children;

namespace Sangam.MemberFamily.Infrastructure.Persistence.Configurations;

public sealed class ChildProfileConfiguration : IEntityTypeConfiguration<ChildProfile>
{
    public void Configure(EntityTypeBuilder<ChildProfile> builder)
    {
        builder.ToTable("child_profiles");

        builder.HasKey(c => c.Id);

        // Domain-assigned, like every other id here. See FamilyConfiguration
        // for what goes wrong when this is left as ValueGeneratedOnAdd.
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.FamilyId).IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.DateOfBirth).IsRequired();
        // No max length: PhotoImageId is a Guid pointing at stored_images.
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.Property(c => c.Gender).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Owned rather than a table of its own: this consent has no life apart
        // from the record it justifies, and is deleted with it.
        builder.OwnsOne(c => c.ParentalConsent, consent =>
        {
            consent.Property(x => x.GivenByMemberId).HasColumnName("parental_consent_given_by");
            consent.Property(x => x.NoticeVersion)
                .HasColumnName("parental_consent_notice_version").HasMaxLength(40);
            consent.Property(x => x.Attestation)
                .HasColumnName("parental_consent_attestation").HasMaxLength(1000);
            consent.Property(x => x.GivenAt).HasColumnName("parental_consent_given_at");
        });

        builder.HasIndex(c => c.FamilyId);

        // The admin's eligibility list scans a Samaaj by date of birth.
        builder.HasIndex(c => new { c.TenantId, c.DateOfBirth });

        builder.Ignore(c => c.DomainEvents);
    }
}

public sealed class ChildConversionRequestConfiguration
    : IEntityTypeConfiguration<ChildConversionRequest>
{
    public void Configure(EntityTypeBuilder<ChildConversionRequest> builder)
    {
        builder.ToTable("child_conversion_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.ChildProfileId).IsRequired();
        builder.Property(r => r.RequestedByMemberId).IsRequired();
        builder.Property(r => r.MobileOrEmail).HasMaxLength(320).IsRequired();
        builder.Property(r => r.RequestedAt).IsRequired();
        // The same number the validator uses, from the aggregate that owns it.
        builder.Property(r => r.DecisionNote)
            .HasMaxLength(ChildConversionRequest.MaxDecisionNoteLength);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne<ChildProfile>()
            .WithMany()
            .HasForeignKey(r => r.ChildProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // At most one undecided request per child. The handler checks this too,
        // but the index is what holds if two family members click at once.
        builder.HasIndex(r => r.ChildProfileId)
            .IsUnique()
            .HasFilter("status = 'Pending'");

        builder.HasIndex(r => new { r.TenantId, r.Status });

        builder.Ignore(r => r.DomainEvents);
    }
}
