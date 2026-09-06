using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.MasterData.Domain.Locations;

namespace Sangam.MasterData.Infrastructure.Persistence.Configurations;

public sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("cities");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);

        // No FK constraint to lookup_items: CountryId is a plain reference to
        // another aggregate in this same DbContext, kept loose on purpose so a
        // country can be deactivated without a cascading delete rule to design.
        builder.HasIndex(c => new { c.CountryId, c.Name });

        builder.Ignore(c => c.DomainEvents);
    }
}
