using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(2000).IsRequired();
        builder.Property(n => n.SourceMessageId).IsRequired();
        builder.Property(n => n.CreatedAt).IsRequired();

        builder.Property(n => n.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // 320 is the longest an email address can be: 64 for the local part, an
        // '@', and 255 for the domain. A mobile number is far shorter.
        builder.Property(n => n.Destination).HasMaxLength(320);
        builder.Property(n => n.FailureReason).HasMaxLength(500);
        builder.Property(n => n.DeliveryAttempts).IsRequired();

        // One notification per source event *per channel*, so a redelivery
        // cannot send the same welcome message twice - while still letting one
        // event raise an in-app notification and an emailed copy of it, which
        // are two different messages to the same person.
        //
        // The channel is part of the key rather than a second table because the
        // guarantee wanted here is exactly "this event, this transport, once",
        // and a unique index is the only way to hold it under a redelivery that
        // arrives while the first copy is still being written.
        builder.HasIndex(n => new { n.SourceMessageId, n.Channel }).IsUnique();

        builder.HasIndex(n => new { n.TenantId, n.RecipientUserId, n.CreatedAt });

        // The dispatcher's claim scans for Pending in creation order, and its
        // read-back looks the batch up by claim id. Both run every poll.
        builder.HasIndex(n => new { n.Status, n.CreatedAt });
        builder.HasIndex(n => n.DeliveryClaimId);

        builder.Ignore(n => n.DomainEvents);
    }
}
