using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Infrastructure.Persistence.Configurations;

public sealed class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> builder)
    {
        builder.ToTable("notification_reads");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.NotificationId).IsRequired();
        builder.Property(r => r.UserId).IsRequired();
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.ReadAt).IsRequired();

        // One read per person per message, and this is the guarantee rather than
        // the handler's check. Opening the same notification in two tabs at once
        // gets past any check written in application code; TryRecordReadAsync
        // leans on this index to make the loser a no-op instead of a duplicate.
        builder.HasIndex(r => new { r.NotificationId, r.UserId }).IsUnique();

        // The member's unread count reads by user, and erasure deletes by user.
        builder.HasIndex(r => r.UserId);

        // Cascade, so deleting a notification cannot leave read rows pointing at
        // nothing. Erasure deletes notifications outright, and a row recording
        // that somebody read a message that no longer exists is a fragment of a
        // person nobody meant to keep.
        builder.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(r => r.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
