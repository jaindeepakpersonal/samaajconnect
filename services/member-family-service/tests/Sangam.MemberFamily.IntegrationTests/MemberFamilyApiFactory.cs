using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sangam.MemberFamily.Infrastructure.Persistence;
using Sangam.MemberFamily.Infrastructure.Security;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sangam.MemberFamily.IntegrationTests;

/// <summary>
/// Hosts the real composition root against a real Postgres and a real Kafka.
/// </summary>
/// <remarks>
/// The broker is real because the claim being tested is a cross-service one:
/// registering in identity-tenant-service produces a profile here, with no
/// synchronous call between them. A fake broker would let that pass while the
/// consumer or the subscription were broken.
/// </remarks>
public sealed class MemberFamilyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-chars";

    /// <summary>Topics the consumer's regex subscription must find at startup.</summary>
    private static readonly string[] SeedTopics = ["identity.user.registered.v1"];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("samaajconnect_member_family")
        .WithUsername("samaajconnect")
        .WithPassword("samaajconnect")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.6.1")
        .Build();

    public string BootstrapServers => _kafka.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        // Created before the host starts: Kafka only matches a regex
        // subscription against topics it already knows about, so seeding them
        // first keeps these tests off the metadata-refresh clock.
        await CreateTopicsAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _kafka.DisposeAsync().AsTask());
    }

    private async Task CreateTopicsAsync()
    {
        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();

        try
        {
            await admin.CreateTopicsAsync(SeedTopics.Select(topic => new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1,
            }));
        }
        catch (CreateTopicsException exception)
            when (exception.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            // Fine - a previous run in the same container left them behind.
        }
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
                ["Kafka:BootstrapServers"] = BootstrapServers,
                // A fresh group per run so each test class reads from the
                // beginning instead of inheriting another run's offsets.
                ["Consumer:GroupId"] = $"members-tests-{Guid.NewGuid():n}",
                ["Consumer:MetadataRefreshIntervalMilliseconds"] = "1000",
            });
        });
    }

    public IProducer<string, string> CreateProducer() =>
        new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = BootstrapServers, Acks = Acks.All }).Build();

    public async Task<T> WithDbContextAsync<T>(Func<MemberFamilyDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        return await action(dbContext);
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds. The consumer is a
    /// background loop, so the alternative is a fixed sleep long enough to be
    /// slow and short enough to be flaky.
    /// </summary>
    public async Task<T> EventuallyAsync<T>(
        Func<MemberFamilyDbContext, Task<T>> query,
        Func<T, bool> condition,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        T latest = default!;

        while (DateTime.UtcNow < deadline)
        {
            latest = await WithDbContextAsync(query);

            if (condition(latest))
            {
                return latest;
            }

            await Task.Delay(250);
        }

        return latest;
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
