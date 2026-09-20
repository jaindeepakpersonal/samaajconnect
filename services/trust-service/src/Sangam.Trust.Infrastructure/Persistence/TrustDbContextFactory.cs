using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sangam.Trust.Application.Abstractions;

namespace Sangam.Trust.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time, so scaffolding a migration does not
/// boot the Api host and start a Kafka consumer.
/// </summary>
public sealed class TrustDbContextFactory : IDesignTimeDbContextFactory<TrustDbContext>
{
    public TrustDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=samaajconnect_trust;Username=samaajconnect;Password=samaajconnect";

        var options = new DbContextOptionsBuilder<TrustDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TrustDbContext(options, new DesignTimeTenantContext());
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
