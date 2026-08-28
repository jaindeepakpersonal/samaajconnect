using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Infrastructure.Persistence.Configurations;

public sealed class MemberProfileConfiguration : IEntityTypeConfiguration<MemberProfile>
{
    public void Configure(EntityTypeBuilder<MemberProfile> builder)
    {
        builder.ToTable("member_profiles");

        // The key is the user id from identity-tenant-service, so it is never
        // generated here.
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.FullName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.PhotoUrl).HasMaxLength(2048);
        builder.Property(p => p.Mobile).HasMaxLength(20);
        builder.Property(p => p.Email).HasMaxLength(320);
        builder.Property(p => p.Address).HasMaxLength(500);
        builder.Property(p => p.Locality).HasMaxLength(120);
        builder.Property(p => p.Profession).HasMaxLength(120);
        builder.Property(p => p.CreatedAt).IsRequired();

        builder.Property(p => p.Gender).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Owned rather than a table of its own: five levels that only ever
        // travel with their profile.
        builder.OwnsOne(p => p.Privacy, privacy =>
        {
            privacy.Property(x => x.Mobile).HasColumnName("privacy_mobile")
                .HasConversion<string>().HasMaxLength(20).IsRequired();
            privacy.Property(x => x.Email).HasColumnName("privacy_email")
                .HasConversion<string>().HasMaxLength(20).IsRequired();
            privacy.Property(x => x.Address).HasColumnName("privacy_address")
                .HasConversion<string>().HasMaxLength(20).IsRequired();
            privacy.Property(x => x.Profession).HasColumnName("privacy_profession")
                .HasConversion<string>().HasMaxLength(20).IsRequired();
            privacy.Property(x => x.DateOfBirth).HasColumnName("privacy_date_of_birth")
                .HasConversion<string>().HasMaxLength(20).IsRequired();
        });

        builder.Navigation(p => p.Privacy).IsRequired();

        // The directory is searched a Samaaj at a time, by name and locality.
        builder.HasIndex(p => new { p.TenantId, p.FullName });
        builder.HasIndex(p => new { p.TenantId, p.Locality });

        builder.Ignore(p => p.DomainEvents);
    }
}
