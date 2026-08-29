using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sangam.Events.Application.Abstractions;

namespace Sangam.Events.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time, so scaffolding a migration does not
/// boot the Api host and start a Kafka consumer.
/// </summary>
public sealed class EventsDbContextFactory : IDesignTimeDbContextFactory<EventsDbContext>
{
    public EventsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=samaajconnect_events;Username=samaajconnect;Password=samaajconnect";

        var options = new DbContextOptionsBuilder<EventsDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new EventsDbContext(options, new DesignTimeTenantContext());
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
