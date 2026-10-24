using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Competition.Domain.Media;
using CompetitionAggregate = Sangam.Competition.Domain.Competitions.Competition;

namespace Sangam.Competition.Infrastructure.Persistence.Configurations;

public sealed class CompetitionPhotoConfiguration : IEntityTypeConfiguration<CompetitionPhoto>
{
    public void Configure(EntityTypeBuilder<CompetitionPhoto> builder)
    {
        builder.ToTable("competition_photos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Bytes).IsRequired();
        builder.Property(p => p.ContentHash).IsRequired().HasMaxLength(64);

        // Deleting a competition takes its photo with it; nothing else points here.
        builder.HasOne<CompetitionAggregate>()
            .WithMany()
            .HasForeignKey(p => p.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.TenantId, p.CompetitionId });
    }
}
