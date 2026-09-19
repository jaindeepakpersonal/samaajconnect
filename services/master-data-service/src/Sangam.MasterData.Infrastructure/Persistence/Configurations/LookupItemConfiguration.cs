using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.MasterData.Domain.Lookups;
using Sangam.MasterData.Domain.Seeding;

namespace Sangam.MasterData.Infrastructure.Persistence.Configurations;

public sealed class LookupItemConfiguration : IEntityTypeConfiguration<LookupItem>
{
    public void Configure(EntityTypeBuilder<LookupItem> builder)
    {
        builder.ToTable("lookup_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.Code).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Label).IsRequired().HasMaxLength(200);

        // One code per category - "IN" means one thing among countries and
        // may mean a different thing among profession codes.
        builder.HasIndex(i => new { i.Category, i.Code }).IsUnique();

        builder.Ignore(i => i.DomainEvents);

        // Default rows, applied by the same migration every service already
        // runs on startup in Development - see DefaultMasterData's own
        // remarks for why these are silent (no Create(), no domain event).
        builder.HasData(DefaultMasterData.LookupItems);
    }
}
