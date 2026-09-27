using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Infrastructure.Persistence;
using Xunit;

namespace Sangam.Trust.IntegrationTests;

/// <summary>The ChaseCollections migration against pledges recorded before pledges were chased.</summary>
/// <remarks>
/// Every other test starts from an empty, fully migrated database, so none would
/// notice if this migration skipped an outstanding pledge or chased one already
/// collected. This one migrates a database of its own to the migration before,
/// writes legacy rows, then runs the rest.
/// </remarks>
public sealed class ChaseMigrationTests(TrustApiFactory factory) : IClassFixture<TrustApiFactory>
{
    [Fact]
    public async Task Every_outstanding_pledge_gets_one_amount_due_and_nothing_else_does()
    {
        var database = "trust_migration_" + Guid.NewGuid().ToString("N")[..8];
        var connectionString = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { Database = database }
            .ConnectionString;

        await using (var admin = new NpgsqlConnection(factory.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        await using var db = new TrustDbContext(
            new DbContextOptionsBuilder<TrustDbContext>()
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
                .UseSnakeCaseNamingConvention()
                .Options,
            new NoTenant());

        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(all[all.IndexOf(all.Single(m => m.EndsWith("_ChaseCollections"))) - 1]);

        var (tenant, trust) = (Guid.NewGuid(), Guid.NewGuid());
        var (memberPledge, namedPledge, collected) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var donor = Guid.NewGuid();

        // Every value is a Guid this test generated, never input.
        var legacyRows = $"""
            INSERT INTO trust_donations
                (id, tenant_id, trust_id, donor_member_id, donor_name, amount, purpose, mode, status,
                 receipt_number, recorded_by, created_at)
            VALUES
                ('{memberPledge}', '{tenant}', '{trust}', '{donor}', NULL, 500000, 'Temple', 'Cash', 'Pledged',
                 NULL, '{Guid.NewGuid()}', now()),
                ('{namedPledge}', '{tenant}', '{trust}', NULL, 'Shri Mahendra Jain', 700000, 'Books', 'Upi', 'Pledged',
                 NULL, '{Guid.NewGuid()}', now()),
                ('{collected}', '{tenant}', '{trust}', '{donor}', NULL, 100000, 'Lamp', 'Cash', 'Collected',
                 1, '{Guid.NewGuid()}', now());
            """;

        await db.Database.ExecuteSqlRawAsync(legacyRows);

        await migrator.MigrateAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand(
            "SELECT source_id, payer_member_id, payer_name, amount, status FROM trust_collections", connection);

        var rows = new List<(Guid Source, Guid? Payer, string? Name, long Amount, string Status)>();

        await using (var reader = await query.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetString(4)));
            }
        }

        rows.Should().HaveCount(2, "the collected donation is not owed");
        rows.Should().Contain((memberPledge, donor, null, 500_000L, "Due"));
        rows.Should().Contain((namedPledge, null, "Shri Mahendra Jain", 700_000L, "Due"));
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsOverride => false;

        public bool HasTenantConflict => false;

        public Guid RequireTenantId() => throw new InvalidOperationException("No tenant in a migration test.");
    }
}
