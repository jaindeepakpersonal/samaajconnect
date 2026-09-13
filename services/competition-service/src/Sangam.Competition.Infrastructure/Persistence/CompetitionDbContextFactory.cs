using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sangam.Competition.Application.Abstractions;

namespace Sangam.Competition.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time, so scaffolding a migration does not
/// boot the Api host and start a Kafka consumer.
/// </summary>
public sealed class CompetitionDbContextFactory : IDesignTimeDbContextFactory<CompetitionDbContext>
{
    public CompetitionDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=samaajconnect_competition;Username=samaajconnect;Password=samaajconnect";

        var options = new DbContextOptionsBuilder<CompetitionDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CompetitionDbContext(options, new DesignTimeTenantContext());
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
