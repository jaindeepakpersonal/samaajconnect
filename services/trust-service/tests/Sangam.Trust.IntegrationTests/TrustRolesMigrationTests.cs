using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Infrastructure.Persistence;
using Xunit;

namespace Sangam.Trust.IntegrationTests;

/// <summary>The TrustRoles migration against rosters and elections that used free-text titles.</summary>
/// <remarks>
/// Every other test starts from an empty, fully migrated database, so none would
/// notice if this migration dropped a trustee's title or left an election for
/// no role. This one migrates a database of its own to the migration before,
/// writes legacy rows, then runs the rest - the ChaseMigrationTests shape.
/// </remarks>
public sealed class TrustRolesMigrationTests(TrustApiFactory factory) : IClassFixture<TrustApiFactory>
{
    [Fact]
    public async Task Titles_become_roles_and_every_row_keeps_what_it_held()
    {
        var database = "trust_roles_migration_" + Guid.NewGuid().ToString("N")[..8];
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
        await migrator.MigrateAsync(all[all.IndexOf(all.Single(m => m.EndsWith("_TrustRoles"))) - 1]);

        var (tenant, trust) = (Guid.NewGuid(), Guid.NewGuid());
        var (plain, treasurer1, treasurer2, removedSecretary) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (electionForTreasurer, electionForPresident, winner, voter, candidate) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var actor = Guid.NewGuid();

        // Every value is a Guid this test generated, never input. Two trustees
        // carry " Treasurer" and "treasurer" - one role, two seats. A removed
        // trustee was Secretary - the role is kept with one seat and nobody in
        // it. An election was for President, which nobody on the roster holds.
        var legacyRows = $"""
            INSERT INTO trusts (id, tenant_id, locality_id, name, status, created_at)
            VALUES ('{trust}', '{tenant}', '{Guid.NewGuid()}', 'Hiran Magri Trust', 'Active', now());

            INSERT INTO trust_members
                (id, tenant_id, trust_id, member_id, position_title, status, added_by, added_at, removed_by, removed_at)
            VALUES
                ('{plain}', '{tenant}', '{trust}', '{Guid.NewGuid()}', NULL, 'Active', '{actor}', now(), NULL, NULL),
                ('{treasurer1}', '{tenant}', '{trust}', '{winner}', ' Treasurer', 'Active', '{actor}', now(), NULL, NULL),
                ('{treasurer2}', '{tenant}', '{trust}', '{Guid.NewGuid()}', 'treasurer', 'Active', '{actor}', now(), NULL, NULL),
                ('{removedSecretary}', '{tenant}', '{trust}', '{Guid.NewGuid()}', 'Secretary', 'Removed', '{actor}', now(), '{actor}', now());

            INSERT INTO trust_elections
                (id, tenant_id, trust_id, position_title, nomination_start_at, nomination_end_at,
                 voting_start_at, voting_end_at, status, created_at, closed_at, winner_member_id, published_at)
            VALUES
                ('{electionForTreasurer}', '{tenant}', '{trust}', 'Treasurer', now(), now(), now(), now(),
                 'Published', now(), now(), '{winner}', now()),
                ('{electionForPresident}', '{tenant}', '{trust}', 'President', now(), now(), now(), now(),
                 'Draft', now(), NULL, NULL, NULL);

            INSERT INTO trust_election_votes (id, election_id, candidate_id, voter_member_id, created_at)
            VALUES ('{Guid.NewGuid()}', '{electionForTreasurer}', '{candidate}', '{voter}', now());
            """;

        await db.Database.ExecuteSqlRawAsync(legacyRows);

        await migrator.MigrateAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var roles = await ReadAsync(connection,
            "SELECT id, title, kind, seats FROM trust_roles WHERE trust_id = @trust",
            trust,
            r => (Id: r.GetGuid(0), Title: r.GetString(1), Kind: r.GetString(2), Seats: r.IsDBNull(3) ? (int?)null : r.GetInt32(3)));

        roles.Select(r => (r.Title.ToLowerInvariant(), r.Kind, r.Seats)).Should().BeEquivalentTo(new[]
        {
            ("trustee", "Trustee", (int?)null),
            ("treasurer", "Elected", (int?)2),
            ("secretary", "Elected", (int?)1),
            ("president", "Elected", (int?)1),
        });

        var roleOf = roles.ToDictionary(r => r.Title.ToLowerInvariant(), r => r.Id);

        var members = (await ReadAsync(connection,
            "SELECT id, role_id FROM trust_members WHERE trust_id = @trust",
            trust,
            r => (Id: r.GetGuid(0), Role: r.GetGuid(1)))).ToDictionary(m => m.Id, m => m.Role);

        members[plain].Should().Be(roleOf["trustee"], "no title means plain Trustee");
        members[treasurer1].Should().Be(roleOf["treasurer"]);
        members[treasurer2].Should().Be(roleOf["treasurer"], "titles match whatever their case and spacing");
        members[removedSecretary].Should().Be(roleOf["secretary"]);

        var elections = (await ReadAsync(connection,
            "SELECT id, role_id, seats, winner_member_ids FROM trust_elections WHERE trust_id = @trust",
            trust,
            r => (Id: r.GetGuid(0), Role: r.GetGuid(1), Seats: r.GetInt32(2), Winners: r.GetFieldValue<Guid[]>(3))))
            .ToDictionary(e => e.Id);

        elections[electionForTreasurer].Role.Should().Be(roleOf["treasurer"]);
        elections[electionForTreasurer].Seats.Should().Be(1, "every election before this filled one seat");
        elections[electionForTreasurer].Winners.Should().Equal(winner);
        elections[electionForPresident].Role.Should().Be(roleOf["president"]);
        elections[electionForPresident].Winners.Should().BeEmpty();

        var choices = await ReadAsync(connection,
            "SELECT choice FROM trust_election_votes WHERE election_id = @trust",
            electionForTreasurer,
            r => r.GetInt32(0));

        choices.Should().Equal(1);
    }

    private static async Task<List<T>> ReadAsync<T>(
        NpgsqlConnection connection, string sql, Guid parameter, Func<NpgsqlDataReader, T> map)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("trust", parameter);

        var rows = new List<T>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsOverride => false;

        public bool HasTenantConflict => false;

        public Guid RequireTenantId() => throw new InvalidOperationException("No tenant in a migration test.");
    }
}
