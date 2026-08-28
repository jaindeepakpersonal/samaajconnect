using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Sangam.IdentityTenant.Infrastructure.Messaging;
using Sangam.IdentityTenant.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Hosts the real composition root from Program.cs against a real Postgres in a
/// container. Only the Kafka producer is substituted — everything the Outbox and
/// the tenant query filter depend on is genuine, because those two are exactly
/// where mocks hide bugs (CLAUDE.md section 9).
/// </summary>
public sealed class IdentityTenantApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-chars";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("samaajconnect_identity")
        .WithUsername("samaajconnect")
        .WithPassword("samaajconnect")
        .Build();

    public RecordingEventPublisher Publisher { get; } = new();

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
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(Publisher);

            // The dispatcher runs on a timer; these tests drive it explicitly so
            // assertions never race a background poll. Removing it by exact type
            // rather than clearing IHostedService, which would also unregister
            // the web host itself.
            var dispatcher = services.SingleOrDefault(d => d.ImplementationType == typeof(OutboxDispatcher));

            if (dispatcher is not null)
            {
                services.Remove(dispatcher);
            }
        });
    }

    public async Task<T> WithDbContextAsync<T>(Func<IdentityTenantDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityTenantDbContext>();

        return await action(dbContext);
    }

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityTenantDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE tenants, outbox_messages RESTART IDENTITY CASCADE;");

        Publisher.Clear();
    }

    /// <summary>Runs one dispatcher cycle against the real database.</summary>
    public async Task<int> DispatchOutboxAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(Services);

        return await dispatcher.DispatchBatchAsync(CancellationToken.None);
    }

    public HttpClient CreateClientWith(params string[] permissions) =>
        CreateClientAs(Guid.NewGuid(), ["SuperAdmin"], permissions);

    public HttpClient CreateClientAs(Guid userId, string[] roles, string[] permissions)
    {
        var client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken(userId, roles, permissions));

        return client;
    }

    private static string CreateToken(Guid userId, string[] roles, string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
        };

        claims.AddRange(roles.Select(r => new Claim("role", r)));
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));

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
