using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sangam.Matrimony.Infrastructure.Persistence;
using Sangam.Matrimony.Infrastructure.Security;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sangam.Matrimony.IntegrationTests;

/// <summary>
/// Hosts the real composition root against a real Postgres.
/// </summary>
/// <remarks>
/// The Kafka broker address below points nowhere on purpose, the same as
/// volunteer-groups-service's own factory: what these tests assert about the
/// outbox is the *row*, written in the same transaction as the state change,
/// and a dispatcher or consumer that actually reached a broker could ship or
/// consume it before the assertion ran. This service does consume
/// `identity.user.erased.v1` in production - its own `IntegrationEventConsumer`
/// is still registered and will retry against the dead address in the
/// background during these tests, which is harmless and matches how the same
/// dispatcher already behaves.
/// </remarks>
public sealed class MatrimonyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-chars";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("samaajconnect_matrimony")
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
        var dbContext = scope.ServiceProvider.GetRequiredService<MatrimonyDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE proposals, biodata_profiles, matrimony_eligibility, outbox_messages RESTART IDENTITY CASCADE;");
    }

    /// <summary>
    /// Records a member's marital-status change the way the Kafka consumer
    /// does - through `RecordMaritalStatusCommand` - so a test can make
    /// somebody eligible, or not, without a broker.
    /// </summary>
    public async Task SetEligibleAsync(Guid memberId, Guid tenantId, bool eligible, DateTimeOffset? at = null)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            memberId,
            tenantId,
            eligibleForMatrimony = eligible,
            changedBy = memberId,
            occurredAt = at ?? DateTimeOffset.UtcNow,
        });

        var result = await SendAsync(new Sangam.Matrimony.Application.IntegrationEvents.Commands.RecordMaritalStatus
            .RecordMaritalStatusCommand(new Sangam.Matrimony.Application.IntegrationEvents.IntegrationEventEnvelope(
                Guid.NewGuid(), tenantId, "members.marital-status.changed.v1", "MemberMaritalStatusChangedDomainEvent",
                payload, DateTimeOffset.UtcNow)));

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Recording eligibility failed: {result.Error.Code}");
        }
    }

    public async Task<T> WithDbContextAsync<T>(Func<MatrimonyDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MatrimonyDbContext>();

        return await action(dbContext);
    }

    /// <summary>
    /// Sends a command/query straight through MediatR, bypassing HTTP and
    /// Kafka both - for exercising the erasure consumer's own handler
    /// directly, the same way its production caller (the Kafka consumer)
    /// does, without needing a real broker in the test.
    /// </summary>
    public async Task<TResult> SendAsync<TResult>(MediatR.IRequest<TResult> request)
    {
        await using var scope = Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();

        return await sender.Send(request);
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
