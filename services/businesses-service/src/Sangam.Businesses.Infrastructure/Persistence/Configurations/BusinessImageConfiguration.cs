using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Businesses.Domain.Businesses;
using Sangam.Businesses.Domain.Media;

namespace Sangam.Businesses.Infrastructure.Persistence.Configurations;

public sealed class BusinessImageConfiguration : IEntityTypeConfiguration<BusinessImage>
{
    public void Configure(EntityTypeBuilder<BusinessImage> builder)
    {
        builder.ToTable("business_images");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.ContentType).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Bytes).IsRequired();
        builder.Property(i => i.ContentHash).IsRequired().HasMaxLength(64);

        // Deleting a business - which only account erasure does - takes every
        // image of it and its products with it, in the database, without the
        // erasure consumer having to load a single one.
        builder.HasOne<Business>()
            .WithMany()
            .HasForeignKey(i => i.BusinessId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.TenantId, i.BusinessId });
    }
}
