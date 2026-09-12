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
        // No max length: PhotoImageId, GautraId, ProfessionId and
        // QualificationId are Guids - the latter three pointing at
        // master-data-service, held as plain foreign keys with no referential
        // integrity across the service boundary.
        builder.Property(p => p.Mobile).HasMaxLength(20);
        builder.Property(p => p.Email).HasMaxLength(320);
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

        // Owned rather than a table of its own: six levels that only ever
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
            privacy.Property(x => x.Qualification).HasColumnName("privacy_qualification")
                .HasConversion<string>().HasMaxLength(20).IsRequired();
        });

        builder.Navigation(p => p.Privacy).IsRequired();

        // One shape (ProfileAddress), owned twice - the same CLR type can be
        // owned more than once as long as each OwnsOne call names its own
        // columns, which these do.
        builder.OwnsOne(p => p.PermanentAddress, address =>
        {
            address.Property(x => x.Line).HasColumnName("permanent_address_line").HasMaxLength(500);
            address.Property(x => x.CityId).HasColumnName("permanent_address_city_id");
            address.Property(x => x.LocalityId).HasColumnName("permanent_address_locality_id");
        });

        builder.Navigation(p => p.PermanentAddress).IsRequired();

        builder.OwnsOne(p => p.TemporaryAddress, address =>
        {
            address.Property(x => x.Line).HasColumnName("temporary_address_line").HasMaxLength(500);
            address.Property(x => x.CityId).HasColumnName("temporary_address_city_id");
            address.Property(x => x.LocalityId).HasColumnName("temporary_address_locality_id");
        });

        // TemporaryAddress itself stays optional - unlike Permanent, above,
        // "no temporary address" is a real state, not an unset one.

        // The directory is searched a Samaaj at a time, by name. It used to
        // also carry a (TenantId, Locality) index for the free-text locality
        // filter; a composite index spanning the parent's TenantId and an
        // owned type's own column needs more than HasIndex's usual lambda
        // shape, and at this platform's scale (a few thousand members per
        // Samaaj, the same reasoning member-family-service's own CLAUDE.md
        // already gives for keeping photos in this database rather than an
        // object store) a plain filter on PermanentAddress_LocalityId costs
        // nothing worth indexing away yet.
        builder.HasIndex(p => new { p.TenantId, p.FullName });

        builder.Ignore(p => p.DomainEvents);
    }
}
