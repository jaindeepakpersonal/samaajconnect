using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Trust.Domain.Donations;
using Sangam.Trust.Domain.Expenses;

namespace Sangam.Trust.Infrastructure.Persistence.Configurations;

// Collection is referenced as `Domain.Collections.Collection`, never bare -
// see that type's own remarks.

public sealed class DonationConfiguration : IEntityTypeConfiguration<Donation>
{
    public void Configure(EntityTypeBuilder<Donation> builder)
    {
        builder.ToTable("trust_donations");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.DonorName).HasMaxLength(200);
        builder.Property(d => d.DonorPhone).HasMaxLength(20);
        builder.Property(d => d.Purpose).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);

        // The receipt sequence's backstop. ReserveNextReceiptNumberAsync
        // serialises the numbering under an advisory lock; this index is what
        // still refuses a duplicate if a caller ever reserved outside a
        // transaction, where that lock is released the moment it is taken.
        // A receipt number handed to a donor has to name exactly one donation.
        builder.HasIndex(d => new { d.TrustId, d.ReceiptNumber })
            .IsUnique()
            .HasFilter("receipt_number IS NOT NULL");

        builder.HasIndex(d => new { d.TrustId, d.CreatedAt });

        builder.Ignore(d => d.DomainEvents);
    }
}

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("trust_expenses");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Category).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Vendor).HasMaxLength(200);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.TrustId, e.RecordedAt });

        builder.Ignore(e => e.DomainEvents);
    }
}

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Domain.Collections.Collection>
{
    public void Configure(EntityTypeBuilder<Domain.Collections.Collection> builder)
    {
        builder.ToTable("trust_collections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.SourceType).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Mode).HasMaxLength(50);
        builder.Property(c => c.PayerName).HasMaxLength(200);

        // One Collection per source, ever. This is what makes consuming
        // boli.result.published.v1 safe under at-least-once delivery - see
        // ICollectionRepository.TryOpenAsync.
        builder.HasIndex(c => new { c.SourceType, c.SourceId }).IsUnique();

        builder.HasIndex(c => new { c.TrustId, c.CreatedAt });
        builder.HasIndex(c => c.PayerMemberId);

        builder.Ignore(c => c.DomainEvents);
    }
}
