using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.MasterData.Domain.Locations;

namespace Sangam.MasterData.Infrastructure.Persistence.Configurations;

public sealed class StateConfiguration : IEntityTypeConfiguration<State>
{
    public void Configure(EntityTypeBuilder<State> builder)
    {
        builder.ToTable("states");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);

        // No FK constraint to lookup_items - see CityConfiguration's own note;
        // the same reasoning applies here.
        builder.HasIndex(s => new { s.CountryId, s.Name });

        builder.Ignore(s => s.DomainEvents);
    }
}
