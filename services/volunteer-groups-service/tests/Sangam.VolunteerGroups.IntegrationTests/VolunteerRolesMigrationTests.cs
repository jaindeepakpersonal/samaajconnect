using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Infrastructure.Persistence;
using Xunit;

namespace Sangam.VolunteerGroups.IntegrationTests;

/// <summary>
/// The VolunteerRoles migration against rows written the old way: positions as
/// typed text, one winner per election, one vote per voter.
/// </summary>
/// <remarks>
/// Every other test starts from an empty, fully migrated database, so none of
/// them would notice if this migration's data step lost a position, mislabelled
/// a president, or left an election pointing at no role. This one migrates a
/// database of its own to the migration before, writes legacy rows with SQL,
/// then runs the rest.
/// </remarks>
public sealed class VolunteerRolesMigrationTests(VolunteerGroupsApiFactory factory)
    : IClassFixture<VolunteerGroupsApiFactory>
{
    private const string Before = "20260926111012_AddGroupLocality";

    [Fact]
    public async Task Typed_positions_become_roles_and_nothing_is_lost()
    {
        var database = "vg_migration_" + Guid.NewGuid().ToString("N")[..8];
        var connectionString = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { Database = database }
            .ConnectionString;

        await using (var admin = new NpgsqlConnection(factory.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        await using var db = new VolunteerGroupsDbContext(
            new DbContextOptionsBuilder<VolunteerGroupsDbContext>()
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
                .UseSnakeCaseNamingConvention()
                .Options,
            new NoTenant());

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);

        var group = Guid.NewGuid();
        var president = Guid.NewGuid();
        var secretary = Guid.NewGuid();
        var coordinator1 = Guid.NewGuid();
        var coordinator2 = Guid.NewGuid();
        var fakePresident = Guid.NewGuid();
        var plain = Guid.NewGuid();
        var election = Guid.NewGuid();
        var voter = Guid.NewGuid();

        // Every value is a Guid this test generated, never input.
        var legacyRows = $"""
            INSERT INTO volunteer_groups (id, tenant_id, name, status, created_at, president_member_id)
            VALUES ('{group}', '{Guid.NewGuid()}', 'Seva Group', 'Active', now(), '{president}');

            INSERT INTO group_members (id, group_id, member_id, role_position, joined_at) VALUES
                (gen_random_uuid(), '{group}', '{president}', 'President', now()),
                (gen_random_uuid(), '{group}', '{secretary}', ' Secretary ', now()),
                (gen_random_uuid(), '{group}', '{coordinator1}', 'Coordinator', now()),
                (gen_random_uuid(), '{group}', '{coordinator2}', 'coordinator', now()),
                (gen_random_uuid(), '{group}', '{fakePresident}', 'president', now()),
                (gen_random_uuid(), '{group}', '{plain}', NULL, now());

            INSERT INTO group_elections (id, tenant_id, group_id, position_title, nomination_start_at,
                nomination_end_at, voting_start_at, voting_end_at, status, created_at, winner_member_id)
            VALUES ('{election}', '{Guid.NewGuid()}', '{group}', 'Treasurer', now(), now(), now(), now(),
                'Published', now(), '{secretary}');

            INSERT INTO election_votes (id, election_id, candidate_id, voter_member_id, created_at)
            VALUES (gen_random_uuid(), '{election}', '{Guid.NewGuid()}', '{voter}', now());
            """;

        await db.Database.ExecuteSqlRawAsync(legacyRows);

        await migrator.MigrateAsync();

        var roles = await Rows<(string Title, string Kind, int? Seats)>(connectionString,
            $"SELECT title, kind, seats FROM group_roles WHERE group_id = '{group}' ORDER BY title",
            r => (r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2)));

        roles.Should().BeEquivalentTo(new[]
        {
            ("Coordinator", "Elected", (int?)2),
            ("President", "President", (int?)1),
            ("Secretary", "Elected", (int?)1),
            ("Treasurer", "Elected", (int?)1),
            ("Volunteer", "Volunteer", (int?)null),
        }, "each distinct title, whatever its case or spacing, is one role, sized to who held it");

        var held = (await Rows<(Guid Member, string Title)>(connectionString,
            $"""
            SELECT m.member_id, r.title FROM group_members m JOIN group_roles r ON r.id = m.role_id
            WHERE m.group_id = '{group}'
            """,
            r => (r.GetGuid(0), r.GetString(1)))).ToDictionary(x => x.Member, x => x.Title);

        held[president].Should().Be("President");
        held[secretary].Should().Be("Secretary");
        held[coordinator1].Should().Be("Coordinator");
        held[coordinator2].Should().Be("Coordinator");
        held[fakePresident].Should().Be("Volunteer", "labelled president, but not the group's president");
        held[plain].Should().Be("Volunteer");

        var migratedElection = (await Rows<(string Title, int Seats, Guid[] Winners)>(connectionString,
            $"""
            SELECT r.title, e.seats, e.winner_member_ids FROM group_elections e
            JOIN group_roles r ON r.id = e.role_id WHERE e.id = '{election}'
            """,
            r => (r.GetString(0), r.GetInt32(1), r.GetFieldValue<Guid[]>(2)))).Single();

        migratedElection.Title.Should().Be("Treasurer");
        migratedElection.Seats.Should().Be(1);
        migratedElection.Winners.Should().Equal(secretary);

        (await Rows(connectionString, "SELECT choice FROM election_votes", r => r.GetInt32(0)))
            .Should().Equal(1);
    }

    private static async Task<List<T>> Rows<T>(
        string connectionString, string sql, Func<NpgsqlDataReader, T> read)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<T>();

        while (await reader.ReadAsync())
        {
            rows.Add(read(reader));
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
