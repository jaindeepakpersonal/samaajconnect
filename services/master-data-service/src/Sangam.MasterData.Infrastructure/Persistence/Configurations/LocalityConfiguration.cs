using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.MasterData.Domain.Locations;

namespace Sangam.MasterData.Infrastructure.Persistence.Configurations;

public sealed class LocalityConfiguration : IEntityTypeConfiguration<Locality>
{
    public void Configure(EntityTypeBuilder<Locality> builder)
    {
        builder.ToTable("localities");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Name).IsRequired().HasMaxLength(200);

        builder.HasIndex(l => new { l.CityId, l.Name });

        builder.Ignore(l => l.DomainEvents);
    }
}
