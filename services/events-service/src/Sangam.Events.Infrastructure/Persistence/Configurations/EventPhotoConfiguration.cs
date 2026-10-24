using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Events.Domain.Events;
using Sangam.Events.Domain.Media;

namespace Sangam.Events.Infrastructure.Persistence.Configurations;

public sealed class EventPhotoConfiguration : IEntityTypeConfiguration<EventPhoto>
{
    public void Configure(EntityTypeBuilder<EventPhoto> builder)
    {
        builder.ToTable("event_photos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Bytes).IsRequired();
        builder.Property(p => p.ContentHash).IsRequired().HasMaxLength(64);

        // Deleting an event takes its photo with it; nothing else points here.
        builder.HasOne<SamaajEvent>()
            .WithMany()
            .HasForeignKey(p => p.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.TenantId, p.EventId });
    }
}
