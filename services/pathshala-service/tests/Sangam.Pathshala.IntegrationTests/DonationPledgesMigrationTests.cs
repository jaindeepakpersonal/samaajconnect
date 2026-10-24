using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Infrastructure.Persistence;
using Xunit;

namespace Sangam.Pathshala.IntegrationTests;

/// <summary>
/// The DonationPledges migration against a donation recorded before pledges
/// existed.
/// </summary>
/// <remarks>
/// Every such donation was money received with a receipt, so it has to come out
/// Paid, keeping its receipt and mode, with the day it was given as the day it
/// was paid. An empty, fully migrated database would never notice if it came out
/// a pledge instead - and a pledge with a receipt number is the one combination
/// the ledger says cannot exist.
/// </remarks>
public sealed class DonationPledgesMigrationTests(PathshalaApiFactory factory)
    : IClassFixture<PathshalaApiFactory>
{
    [Fact]
    public async Task A_donation_recorded_before_pledges_is_paid_on_the_day_it_was_given()
    {
        var database = "ps_migration_" + Guid.NewGuid().ToString("N")[..8];
        var connectionString = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { Database = database }
            .ConnectionString;

        await using (var admin = new NpgsqlConnection(factory.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        await using var db = new PathshalaDbContext(
            new DbContextOptionsBuilder<PathshalaDbContext>()
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
                .UseSnakeCaseNamingConvention()
                .Options,
            new NoTenant());

        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(all[all.IndexOf(all.Single(m => m.EndsWith("_DonationPledges"))) - 1]);

        var donation = Guid.NewGuid();

        // Every value is one this test chose, never input.
        var legacyRow = $"""
            INSERT INTO pathshala_donations
                (id, tenant_id, pathshala_id, receipt_number, donor_name, amount, donated_on, mode, recorded_by, recorded_at)
            VALUES
                ('{donation}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', 7, 'Shri Mahendra Kothari', 500000,
                 '2026-09-30', 'Upi', '{Guid.NewGuid()}', now());
            """;

        await db.Database.ExecuteSqlRawAsync(legacyRow);

        await migrator.MigrateAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var read = new NpgsqlCommand(
            $"SELECT status, receipt_number, mode, paid_on FROM pathshala_donations WHERE id = '{donation}'", connection);
        await using var reader = await read.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("Paid");
        reader.GetInt32(1).Should().Be(7, "its receipt was handed to somebody");
        reader.GetString(2).Should().Be("Upi");
        reader.GetFieldValue<DateOnly>(3).Should().Be(new DateOnly(2026, 9, 30));
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsOverride => false;

        public bool HasTenantConflict => false;

        public Guid RequireTenantId() => throw new InvalidOperationException("No tenant in a migration test.");
    }
}
