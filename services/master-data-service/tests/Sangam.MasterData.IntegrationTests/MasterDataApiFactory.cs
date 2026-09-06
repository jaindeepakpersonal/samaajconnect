using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sangam.MasterData.Infrastructure.Persistence;
using Sangam.MasterData.Infrastructure.Security;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sangam.MasterData.IntegrationTests;

/// <summary>
/// Hosts the real composition root against a real Postgres.
/// </summary>
/// <remarks>
/// No Kafka here, the same choice celebrity-voting-service's own factory makes
/// and for the same reason: what these tests are actually about is the tenant
/// query filter on Gautra and the unique index on lookup_items, both of which
/// only a real database can prove.
/// </remarks>
public sealed class MasterDataApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-chars";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("samaajconnect_master_data")
        .WithUsername("samaajconnect")
        .WithPassword("samaajconnect")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:Issuer"] = "samaajconnect",
                ["Jwt:Audience"] = "samaajconnect",
                // Goes nowhere on purpose - these tests assert the outbox row
                // itself, and a dispatcher that shipped it would delete the
                // evidence. Same choice celebrity-voting-service's factory makes.
                ["Kafka:BootstrapServers"] = "localhost:1",
            });
        });
    }

    /// <summary>Truncates every table this service owns. Missing one leaks rows between tests.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE gautras, localities, cities, lookup_items, outbox_messages RESTART IDENTITY CASCADE;");
    }

    public async Task<T> WithDbContextAsync<T>(Func<MasterDataDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();

        return await action(dbContext);
    }

    public HttpClient CreateClientAs(Guid userId, Guid tenantId, string[] roles, string[] permissions)
    {
        var client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new("Bearer", CreateToken(userId, tenantId, roles, permissions));

        return client;
    }

    private static string CreateToken(Guid userId, Guid tenantId, string[] roles, string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(PlatformClaimTypes.TenantId, tenantId.ToString()),
        };

        claims.AddRange(roles.Select(r => new Claim(PlatformClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(PlatformClaimTypes.Permission, p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "samaajconnect",
            audience: "samaajconnect",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
