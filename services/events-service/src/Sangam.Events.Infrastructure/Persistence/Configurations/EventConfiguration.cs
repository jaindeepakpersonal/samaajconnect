using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Events.Domain.Events;

namespace Sangam.Events.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<SamaajEvent>
{
    public void Configure(EntityTypeBuilder<SamaajEvent> builder)
    {
        builder.ToTable("events");
        builder.HasKey(e => e.Id);

        // Domain-assigned, like every key on this platform. Left as EF's
        // default, a child added to a tracked parent comes back Modified rather
        // than Added and the save fails against a row that was never there.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(5000);
        builder.Property(e => e.Venue).HasMaxLength(300);
        builder.Property(e => e.CancellationReason).HasMaxLength(1000);
        builder.Property(e => e.OrganizerType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        // The list: this Samaaj's published events, soonest first.
        builder.HasIndex(e => new { e.TenantId, e.Status, e.StartAt });

        // "What is my group holding?"
        builder.HasIndex(e => e.OrganizerId);

        builder.HasMany(e => e.Registrations)
            .WithOne()
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(SamaajEvent.Registrations))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(e => e.DomainEvents);
    }
}

public sealed class EventRegistrationConfiguration : IEntityTypeConfiguration<EventRegistration>
{
    public void Configure(EntityTypeBuilder<EventRegistration> builder)
    {
        builder.ToTable("event_registrations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        // One row per member per event. The aggregate re-uses a cancelled row
        // rather than adding a second, so this is that invariant in the
        // database - and it is what holds if two requests arrive at once.
        builder.HasIndex(r => new { r.EventId, r.MemberId }).IsUnique();

        // The waitlist is read in registration order.
        builder.HasIndex(r => new { r.EventId, r.Status, r.RegisteredAt });
    }
}
