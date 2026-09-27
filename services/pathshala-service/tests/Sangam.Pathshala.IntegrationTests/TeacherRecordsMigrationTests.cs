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
/// The TeachersDonationsAnnouncements migration against assignments written the
/// old way, by member id.
/// </summary>
/// <remarks>
/// Every other test starts from an empty, fully migrated database, so none would
/// notice if this migration lost a teacher or left an assignment pointing at a
/// member id the new foreign key refuses. This one migrates a database of its own
/// to the migration before, writes legacy rows, then runs the rest.
/// </remarks>
public sealed class TeacherRecordsMigrationTests(PathshalaApiFactory factory)
    : IClassFixture<PathshalaApiFactory>
{
    [Fact]
    public async Task Each_teacher_who_taught_becomes_one_record_linked_to_their_login()
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
        await migrator.MigrateAsync(all[all.IndexOf(all.Single(m => m.EndsWith("_TeachersDonationsAnnouncements"))) - 1]);

        var pathshala = Guid.NewGuid();
        var session = Guid.NewGuid();
        var (class8, class9) = (Guid.NewGuid(), Guid.NewGuid());
        var (ramesh, kavita) = (Guid.NewGuid(), Guid.NewGuid());

        // Every value is a Guid this test generated, never input.
        var legacyRows = $"""
            INSERT INTO pathshalas (id, tenant_id, name, status, created_at)
            VALUES ('{pathshala}', '{Guid.NewGuid()}', 'Shri Mahavir Jain Pathshala', 'Active', now());

            INSERT INTO academic_sessions (id, pathshala_id, label, start_date, end_date, is_current)
            VALUES ('{session}', '{pathshala}', '2026-27', '2026-03-01', '2027-02-28', true);

            INSERT INTO classes (id, pathshala_id, session_id, name) VALUES
                ('{class8}', '{pathshala}', '{session}', 'Class 8'),
                ('{class9}', '{pathshala}', '{session}', 'Class 9');

            -- Ramesh teaches two classes, Kavita one: two teachers, three assignments.
            INSERT INTO teacher_assignments (id, class_id, teacher_member_id, assigned_at) VALUES
                (gen_random_uuid(), '{class8}', '{ramesh}', now()),
                (gen_random_uuid(), '{class9}', '{ramesh}', now()),
                (gen_random_uuid(), '{class9}', '{kavita}', now());
            """;

        await db.Database.ExecuteSqlRawAsync(legacyRows);

        await migrator.MigrateAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var teachers = new NpgsqlCommand(
            $"SELECT user_id FROM pathshala_teachers WHERE pathshala_id = '{pathshala}'", connection);
        var logins = new List<Guid>();
        await using (var reader = await teachers.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                logins.Add(reader.GetGuid(0));
            }
        }

        logins.Should().BeEquivalentTo([ramesh, kavita], "one record per teacher, linked to the account they signed in with");

        await using var joined = new NpgsqlCommand(
            """
            SELECT count(*) FROM teacher_assignments a
            JOIN pathshala_teachers t ON t.id = a.teacher_id
            """, connection);

        ((long)(await joined.ExecuteScalarAsync())!).Should().Be(3, "every assignment now names a teacher record");
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsOverride => false;

        public bool HasTenantConflict => false;

        public Guid RequireTenantId() => throw new InvalidOperationException("No tenant in a migration test.");
    }
}
