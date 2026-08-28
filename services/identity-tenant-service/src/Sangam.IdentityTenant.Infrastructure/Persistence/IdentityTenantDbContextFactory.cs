using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sangam.IdentityTenant.Application.Abstractions;

namespace Sangam.IdentityTenant.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time. Without it EF would have to boot
/// the Api host to find a DbContext, which would also start the outbox
/// dispatcher and try to reach Kafka just to scaffold a migration.
/// </summary>
public sealed class IdentityTenantDbContextFactory : IDesignTimeDbContextFactory<IdentityTenantDbContext>
{
    public IdentityTenantDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=samaajconnect_identity;Username=samaajconnect;Password=samaajconnect";

        var options = new DbContextOptionsBuilder<IdentityTenantDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityTenantDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsOverride => false;

        public Guid RequireTenantId() =>
            throw new InvalidOperationException("No tenant context exists at design time.");
    }
}
