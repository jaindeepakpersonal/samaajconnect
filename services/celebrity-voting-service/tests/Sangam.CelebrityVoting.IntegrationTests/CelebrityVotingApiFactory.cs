using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sangam.CelebrityVoting.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Sangam.CelebrityVoting.Infrastructure.Persistence;
using Sangam.CelebrityVoting.Infrastructure.Security;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sangam.CelebrityVoting.IntegrationTests;

/// <summary>
/// Hosts the real composition root against a real Postgres.
/// </summary>
/// <remarks>
/// No Kafka here, unlike member-family-service. This service consumes nothing,
/// and what its tests are actually about are database claims above all else:
/// the unique index on (CampaignId, VoterMemberId) is the double-voting
/// guarantee, and there is no way to demonstrate an index holding under
/// concurrent inserts except by making concurrent inserts against a real
/// database.
/// </remarks>
public sealed class CelebrityVotingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-chars";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("samaajconnect_celebrity_voting")
        .WithUsername("samaajconnect")
        .WithPassword("samaajconnect")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// The clock the host runs on, so a test can move a campaign from
    /// nominations to voting without waiting. See TestClock.
    /// </summary>
    public TestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(Clock);
        });

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
        var dbContext = scope.ServiceProvider.GetRequiredService<CelebrityVotingDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE votes, campaign_results, candidates, voting_campaigns, "
            + "outbox_messages RESTART IDENTITY CASCADE;");
    }

    public async Task<T> WithDbContextAsync<T>(Func<CelebrityVotingDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CelebrityVotingDbContext>();

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
