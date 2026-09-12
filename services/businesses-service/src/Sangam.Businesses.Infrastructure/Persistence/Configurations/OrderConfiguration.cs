using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Businesses.Domain.Orders;

namespace Sangam.Businesses.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.TenantId).IsRequired();
        builder.Property(o => o.BusinessId).IsRequired();
        builder.Property(o => o.BusinessOwnerMemberId).IsRequired();
        builder.Property(o => o.BuyerMemberId).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.Note).HasMaxLength(500);
        builder.Property(o => o.TotalAmountInPaise).IsRequired();
        builder.Property(o => o.PlacedAt).IsRequired();

        builder.OwnsMany(o => o.Items, item =>
        {
            item.ToTable("order_items");
            item.WithOwner().HasForeignKey("order_id");

            // A synthetic key: OrderItem is a plain record with no id of its
            // own (ProductId identifies the product, not this line), so EF
            // needs a shadow key to persist the owned collection.
            item.Property<int>("id");
            item.HasKey("id");

            item.Property(i => i.ProductId).IsRequired();
            item.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
            item.Property(i => i.UnitPriceInPaise).IsRequired();
            item.Property(i => i.Quantity).IsRequired();
        });

        builder.Navigation(o => o.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        // The Placed tab's common case: this buyer's own orders, most recent first.
        builder.HasIndex(o => new { o.TenantId, o.BuyerMemberId });

        // The Received tab's common case: orders against any business this
        // owner runs - a plain filter, no join back to Business needed since
        // the owner id is snapshotted here at placement time.
        builder.HasIndex(o => new { o.TenantId, o.BusinessOwnerMemberId, o.Status });

        builder.Ignore(o => o.DomainEvents);
    }
}
