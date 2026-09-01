using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Boli.Domain.Auctions;

namespace Sangam.Boli.Infrastructure.Persistence.Configurations;

public sealed class OccasionConfiguration : IEntityTypeConfiguration<BoliOccasion>
{
    public void Configure(EntityTypeBuilder<BoliOccasion> builder)
    {
        builder.ToTable("boli_occasions");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.Title).IsRequired().HasMaxLength(200);
        builder.Property(o => o.Description).HasMaxLength(2000);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(o => new { o.TenantId, o.Status });

        builder.HasMany(o => o.Types)
            .WithOne()
            .HasForeignKey(t => t.OccasionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(BoliOccasion.Types))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(o => o.DomainEvents);
    }
}

public sealed class BoliTypeConfiguration : IEntityTypeConfiguration<BoliType>
{
    public void Configure(EntityTypeBuilder<BoliType> builder)
    {
        builder.ToTable("boli_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(120);
        builder.Property(t => t.Description).HasMaxLength(2000);

        // One name per occasion. The aggregate refuses a duplicate; this is what
        // holds if two managers add the same type at once, and two types called
        // "Mangal Deep" would leave every published result ambiguous about which
        // one a Boli belonged to.
        builder.HasIndex(t => new { t.OccasionId, t.Name }).IsUnique();
    }
}

public sealed class BoliLotConfiguration : IEntityTypeConfiguration<Domain.Auctions.Boli>
{
    public void Configure(EntityTypeBuilder<Domain.Auctions.Boli> builder)
    {
        builder.ToTable("boli");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.Title).IsRequired().HasMaxLength(200);
        builder.Property(b => b.EligibilityRule).HasMaxLength(500);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(b => new { b.TenantId, b.Status });
        builder.HasIndex(b => b.OccasionId);

        builder.Ignore(b => b.DomainEvents);
    }
}

public sealed class BidConfiguration : IEntityTypeConfiguration<Bid>
{
    public void Configure(EntityTypeBuilder<Bid> builder)
    {
        builder.ToTable("bids");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        // The correctness guarantee, and the reason this index is unique.
        //
        // Placing a bid is a check-then-insert: read the highest, decide whether
        // the amount clears it, write. The row lock in
        // BoliRepository.LockForBiddingAsync is what serialises that, but a lock
        // is a convention — some future code path can forget to take it, and a
        // second instance of this service would not even notice.
        //
        // Two bids of the same amount on one Boli leave "the highest bid" with no
        // single referent, and therefore leave the winner to be decided by
        // whichever row a query happened to sort first. The database refusing the
        // second one is what makes that impossible rather than unlikely.
        builder.HasIndex(b => new { b.BoliId, b.Amount }).IsUnique();

        // The bid history and the highest-bid lookup both read this way.
        builder.HasIndex(b => new { b.BoliId, b.PlacedAt });
    }
}

public sealed class BoliResultConfiguration : IEntityTypeConfiguration<BoliResult>
{
    public void Configure(EntityTypeBuilder<BoliResult> builder)
    {
        builder.ToTable("boli_results");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // One result per Boli. A second would leave "the result" with no
        // referent, the same way a second published ranking would in
        // celebrity-voting-service.
        builder.HasIndex(r => r.BoliId).IsUnique();

        builder.HasIndex(r => new { r.TenantId, r.PublishedAt });
    }
}
