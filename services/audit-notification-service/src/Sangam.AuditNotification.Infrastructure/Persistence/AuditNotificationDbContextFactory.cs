using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sangam.AuditNotification.Application.Abstractions;

namespace Sangam.AuditNotification.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time, so scaffolding a migration does not
/// boot the Api host and start a Kafka consumer.
/// </summary>
public sealed class AuditNotificationDbContextFactory
    : IDesignTimeDbContextFactory<AuditNotificationDbContext>
{
    public AuditNotificationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=samaajconnect_audit_notification;Username=samaajconnect;Password=samaajconnect";

        var options = new DbContextOptionsBuilder<AuditNotificationDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AuditNotificationDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsOverride => false;

        public bool HasTenantConflict => false;

        public Guid RequireTenantId() =>
            throw new InvalidOperationException("No tenant context exists at design time.");
    }
}
