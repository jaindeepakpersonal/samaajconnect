using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.MasterData.Domain.Gautras;

namespace Sangam.MasterData.Infrastructure.Persistence.Configurations;

public sealed class GautraConfiguration : IEntityTypeConfiguration<Gautra>
{
    public void Configure(EntityTypeBuilder<Gautra> builder)
    {
        builder.ToTable("gautras");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.Name).IsRequired().HasMaxLength(200);

        // One name per Samaaj's own list - two different Samaajs can each have
        // their own Gautra of the same name, but one Samaaj should not list it twice.
        builder.HasIndex(g => new { g.TenantId, g.Name }).IsUnique();

        builder.Ignore(g => g.DomainEvents);
    }
}
