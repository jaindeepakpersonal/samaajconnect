using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sangam.Competition.Infrastructure.Persistence;
using Sangam.Competition.Infrastructure.Security;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sangam.Competition.IntegrationTests;

/// <summary>
/// Hosts the real composition root against a real Postgres.
/// </summary>
/// <remarks>
/// The Kafka broker address below points nowhere on purpose, the same as
/// volunteer-groups-service's own factory: what these tests assert about the
/// outbox is the *row*, written in the same transaction as the state change,
/// and a dispatcher that actually reached a broker could ship it before the
/// assertion ran. This service consumes nothing, so there is no consumer
/// retrying in the background - only the outbox dispatcher.
/// </remarks>
public sealed class CompetitionApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-chars";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("samaajconnect_competition")
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
                // A broker address that goes nowhere. The outbox dispatcher is
                // a background loop and will fail to reach it, which is what we
                // want: these tests assert the outbox *row*, and a dispatcher
                // that shipped it would delete the evidence.
                ["Kafka:BootstrapServers"] = "localhost:1",
            });
        });
    }

    /// <summary>Truncates everything between tests.</summary>
    /// <remarks>
    /// Every table this service owns belongs in the list. A table missing from
    /// it leaks rows between tests, and the symptom is a test that passes alone
    /// and fails in the suite - which is how refresh_tokens announced itself in
    /// identity-tenant-service.
    /// </remarks>
    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CompetitionDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE round_results, scores, entries, rounds, competition_judges, "
            + "competition_photos, competition_registrations, competitions, outbox_messages RESTART IDENTITY CASCADE;");
    }

    public async Task<T> WithDbContextAsync<T>(Func<CompetitionDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CompetitionDbContext>();

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
