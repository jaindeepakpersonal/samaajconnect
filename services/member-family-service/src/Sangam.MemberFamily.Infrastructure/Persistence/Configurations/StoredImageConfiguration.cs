using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.MemberFamily.Domain.Media;

namespace Sangam.MemberFamily.Infrastructure.Persistence.Configurations;

public sealed class StoredImageConfiguration : IEntityTypeConfiguration<StoredImage>
{
    public void Configure(EntityTypeBuilder<StoredImage> builder)
    {
        builder.ToTable("stored_images");

        builder.HasKey(i => i.Id);

        // Domain-assigned, like every other key in this service. Left as EF's
        // default ValueGeneratedOnAdd, an image added to a tracked graph comes
        // back Modified rather than Added and the save fails against a row that
        // was never there - the same trap Family and FamilyMember hit.
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.TenantId).IsRequired();
        builder.Property(i => i.OwnerId).IsRequired();

        // Stored as the enum's name rather than its number. A bytea table is the
        // last place anybody wants to be reading an integer against a C# enum
        // during an incident, and the storage cost beside the bytes is nothing.
        builder.Property(i => i.OwnerKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(i => i.ContentType).HasMaxLength(32).IsRequired();
        builder.Property(i => i.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(i => i.ByteSize).IsRequired();
        builder.Property(i => i.UploadedBy).IsRequired();
        builder.Property(i => i.UploadedAt).IsRequired();

        // Postgres TOASTs anything over ~2KB out of the main heap and compresses
        // it, so the table itself stays small and a query that does not select
        // this column does not read it. That is what makes DescribeAsync cheap
        // and is most of why keeping 2 MB images in the database is workable at
        // this scale at all.
        builder.Property(i => i.Bytes).IsRequired();

        // How erasure finds every image for a person, including any a replace
        // path orphaned. Not unique: an owner may briefly have two rows while a
        // photo is being replaced inside one transaction.
        builder.HasIndex(i => new { i.TenantId, i.OwnerKind, i.OwnerId })
            .HasDatabaseName("ix_stored_images_owner");
    }
}
