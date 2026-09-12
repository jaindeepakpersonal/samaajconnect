using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Businesses.Domain.Businesses;

namespace Sangam.Businesses.Infrastructure.Persistence.Configurations;

public sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> builder)
    {
        builder.ToTable("businesses");

        // Domain-assigned key, never generated here. This repo has hit the
        // ValueGeneratedOnAdd default several times already on a
        // domain-assigned key; this is one more place it must not.
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.OwnerMemberId).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.CategoryId).IsRequired();
        builder.Property(b => b.Description).HasMaxLength(1000);
        builder.Property(b => b.ContactPhone).HasMaxLength(20);
        builder.Property(b => b.ContactEmail).HasMaxLength(200);
        builder.Property(b => b.IsActive).IsRequired();
        builder.Property(b => b.CreatedAt).IsRequired();

        builder.OwnsMany(b => b.Products, product =>
        {
            product.ToTable("business_products");
            product.WithOwner().HasForeignKey("business_id");
            product.HasKey(p => p.Id);
            product.Property(p => p.Id).ValueGeneratedNever();

            product.Property(p => p.Name).HasMaxLength(200).IsRequired();
            product.Property(p => p.Description).HasMaxLength(500);
            product.Property(p => p.PriceInPaise).IsRequired();
            product.Property(p => p.IsAvailable).IsRequired();
            product.Property(p => p.CreatedAt).IsRequired();
        });

        builder.Navigation(b => b.Products).HasField("_products").UsePropertyAccessMode(PropertyAccessMode.Field);

        // The directory's common case: active businesses in this Samaaj,
        // filtered by category and/or location.
        builder.HasIndex(b => new { b.TenantId, b.IsActive, b.CategoryId, b.LocalityId });

        builder.Ignore(b => b.DomainEvents);
    }
}
