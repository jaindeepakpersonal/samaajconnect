using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.AuditNotification.Domain.AuditLogs;

namespace Sangam.AuditNotification.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.SourceMessageId).IsRequired();
        builder.Property(a => a.Topic).HasMaxLength(200).IsRequired();
        builder.Property(a => a.EventType).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(100);
        builder.Property(a => a.EntityId).HasMaxLength(200);
        builder.Property(a => a.ActorRole).HasMaxLength(60);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.AfterState).HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.BeforeState).HasColumnType("jsonb");
        builder.Property(a => a.OccurredAt).IsRequired();
        builder.Property(a => a.RecordedAt).IsRequired();

        // The real guarantee against double-recording a redelivered event. The
        // handler's pre-check is the readable path; this is what holds when two
        // consumer instances race.
        builder.HasIndex(a => a.SourceMessageId).IsUnique();

        // The admin screen reads one Samaaj's trail newest-first.
        builder.HasIndex(a => new { a.TenantId, a.OccurredAt });
        builder.HasIndex(a => new { a.TenantId, a.Action });

        builder.Ignore(a => a.DomainEvents);
    }
}
