using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sangam.Pathshala.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Sangam.Pathshala.Infrastructure.Persistence;
using Sangam.Pathshala.Infrastructure.Security;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sangam.Pathshala.IntegrationTests;

/// <summary>
/// Hosts the real composition root against a real Postgres.
/// </summary>
/// <remarks>
/// No Kafka broker. This service does consume one topic, but the consumer is a
/// thin loop around a command, and the command is what the tests exercise
/// directly - a real broker would add minutes to the suite to prove Confluent
/// can deliver a message.
///
/// A real Postgres is not optional here. What this service most needs proving
/// are database claims: the unique index on (EnrolmentId, ClassDate) is what
/// keeps a register submitted twice from inflating a child's attendance, and
/// there is no way to show an index refusing a duplicate except against a real
/// one.
/// </remarks>
public sealed class PathshalaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-chars";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("samaajconnect_pathshala")
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
    /// a class date to a later one without waiting. See TestClock.
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
        var dbContext = scope.ServiceProvider.GetRequiredService<PathshalaDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE attendance, exam_results, exams, student_enrolments, "
            + "class_schedules, teacher_assignments, classes, academic_sessions, "
            + "pathshalas, outbox_messages RESTART IDENTITY CASCADE;");
    }

    public async Task<T> WithDbContextAsync<T>(Func<PathshalaDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PathshalaDbContext>();

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
