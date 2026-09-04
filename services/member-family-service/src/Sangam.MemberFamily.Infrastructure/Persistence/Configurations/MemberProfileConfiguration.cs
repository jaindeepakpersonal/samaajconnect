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
        // No max length: PhotoImageId is a Guid pointing at stored_images.
        builder.Property(p => p.Mobile).HasMaxLength(20);
        builder.Property(p => p.Email).HasMaxLength(320);
        builder.Property(p => p.Address).HasMaxLength(500);
        builder.Property(p => p.Locality).HasMaxLength(120);
        builder.Property(p => p.Profession).HasMaxLength(120);
        builder.Property(p => p.CreatedAt).IsRequired();

        // Defaulted in the database as well as in the aggregate, so the rows that
        // exist when the column arrives are listed rather than silently
        // disappearing from every Samaaj's directory at once.
        //
        // **ValueGeneratedNever is load-bearing, and it was missing.**
        // HasDefaultValue makes a property ValueGeneratedOnAdd, and EF then
        // leaves a CLR-default value out of the INSERT so the database default
        // can apply. The CLR default of a bool is `false` — which is exactly the
        // value that means "not listed". So inserting an unlisted profile wrote
        // no column at all and the row came back listed, silently, with the
        // aggregate and the database disagreeing. Three integration tests caught
        // it. Updates were never affected, which is what made it a landmine
        // rather than an outage.
        //
        // Same lesson as the ValueGeneratedNever on the Family keys below: when
        // the aggregate owns a value, say so, or EF will decide it owns it too.
        builder.Property(p => p.IsListedInDirectory)
            .IsRequired()
            .HasDefaultValue(true)
            .ValueGeneratedNever();

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
