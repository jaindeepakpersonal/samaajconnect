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

        // One notification per source event, so a redelivery cannot send the
        // same welcome message twice.
        builder.HasIndex(n => n.SourceMessageId).IsUnique();

        builder.HasIndex(n => new { n.TenantId, n.RecipientUserId, n.CreatedAt });

        builder.Ignore(n => n.DomainEvents);
    }
}
