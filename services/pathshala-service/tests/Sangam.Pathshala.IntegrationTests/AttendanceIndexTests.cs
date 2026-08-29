using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.Pathshala.Application.IntegrationEvents;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Enrolments;
using Sangam.Pathshala.Infrastructure.Persistence;
using Xunit;

namespace Sangam.Pathshala.IntegrationTests;

/// <summary>
/// The correctness claim this service turns on: a register submitted twice does
/// not inflate a child's attendance.
/// </summary>
/// <remarks>
/// PathshalaFlowTests proves the behaviour - re-marking amends rather than
/// duplicating. It does not, on its own, prove why: a handler that happened to
/// serialise its callers would pass it, and would stop holding the moment the
/// service ran on two instances or two teachers pressed Submit together.
///
/// These tests name the mechanism. They read the live schema and talk to the
/// table directly, past the handler and past the repository, so the only thing
/// that can make them pass is the unique index being present and being unique.
/// </remarks>
public sealed class AttendanceIndexTests(PathshalaApiFactory factory)
    : IClassFixture<PathshalaApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        factory.Clock.Set(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

        return factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string?> IndexDefinitionAsync(string table, string index) =>
        await factory.WithDbContextAsync(async db =>
        {
            await db.Database.OpenConnectionAsync();

            await using var command = db.Database.GetDbConnection().CreateCommand();

            command.CommandText =
                "SELECT indexdef FROM pg_indexes "
                + $"WHERE tablename = '{table}' AND indexname = '{index}';";

            return (string?)await command.ExecuteScalarAsync();
        });

    [Fact]
    public async Task The_attendance_index_exists_and_is_unique()
    {
        // Read from the live schema rather than from the EF model: the model is
        // what we asked for, this is what the database built.
        var definition = await IndexDefinitionAsync(
            "attendance", "ix_attendance_enrolment_id_class_date");

        definition.Should().NotBeNull(
            "the index on (enrolment_id, class_date) is what keeps one child's "
            + "attendance to one mark per class day");

        definition.Should().StartWith("CREATE UNIQUE INDEX",
            "a non-unique index on those columns would make the attendance query "
            + "fast and would stop preventing anything");
    }

    [Fact]
    public async Task The_exam_result_index_exists_and_is_unique()
    {
        var definition = await IndexDefinitionAsync(
            "exam_results", "ix_exam_results_exam_id_enrolment_id");

        definition.Should().StartWith("CREATE UNIQUE INDEX",
            "two marks for one child in one exam would make the average score "
            + "depend on which row happened to be read");
    }

    [Fact]
    public async Task The_enrolment_index_exists_and_is_unique()
    {
        var definition = await IndexDefinitionAsync(
            "student_enrolments", "ix_student_enrolments_pathshala_id_child_profile_id");

        definition.Should().StartWith("CREATE UNIQUE INDEX",
            "two enrolments for one child would put them on two rolls with two "
            + "attendance records, neither of them right");
    }

    [Fact]
    public async Task A_second_mark_for_one_child_on_one_date_is_refused_by_the_database()
    {
        // Straight at the table, on two separate contexts, so nothing in the
        // application layer can be what refuses the second write.
        var enrolmentId = Guid.NewGuid();
        var classId = Guid.NewGuid();
        var classDate = new DateOnly(2026, 3, 1);
        var now = DateTimeOffset.UtcNow;

        await InsertAsync(new AttendanceEntry(
            TenantId, enrolmentId, classId, classDate, AttendanceStatus.Present, Guid.NewGuid(), now));

        var second = async () => await InsertAsync(new AttendanceEntry(
            TenantId, enrolmentId, classId, classDate, AttendanceStatus.Absent, Guid.NewGuid(), now));

        var thrown = await second.Should().ThrowAsync<DbUpdateException>(
            "the index, not the application, is what refuses it");

        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(
                PostgresErrorCodes.UniqueViolation,
                "EnrolmentRepository.SaveRegisterAsync catches exactly this SQLSTATE "
                + "so a duplicated register reports a correction rather than a 500");

        var rows = await factory.WithDbContextAsync(db =>
            db.Attendance.IgnoreQueryFilters().CountAsync(a => a.EnrolmentId == enrolmentId));

        rows.Should().Be(1);
    }

    private async Task InsertAsync(AttendanceEntry entry)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PathshalaDbContext>();

        db.Attendance.Add(entry);

        await db.SaveChangesAsync();
    }

    // ---- Under real concurrency --------------------------------------------

    [Fact]
    public async Task Ten_simultaneous_submissions_of_one_register_leave_one_row_per_student()
    {
        // The reason the index exists. A teacher on a bad connection presses
        // Submit repeatedly; every one of those requests reads no existing mark
        // before any of them writes.
        var adminId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();

        var admin = factory.CreateClientAs(
            adminId, TenantId, [Roles.SuperAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.PathshalaManage]);

        var created = await admin.PostAsJsonAsync("/v1/pathshala/pathshalas", new
        {
            name = "Concurrency", address = (string?)null, contactPerson = (string?)null,
        });

        var pathshalaId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var opened = await admin.PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{pathshalaId}/sessions",
            new { label = "2026-27", startDate = "2026-03-01", endDate = "2027-02-28" });

        var sessionId = (await opened.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessions").EnumerateArray().First().GetProperty("id").GetGuid();

        var classCreated = await admin.PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{pathshalaId}/classes",
            new { sessionId, name = "Class 8", roomLabel = (string?)null });

        var classId = (await classCreated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await admin.PostAsJsonAsync(
            $"/v1/pathshala/classes/{classId}/teachers",
            new { teacherMemberId = teacherId, assign = true });

        var enrolmentIds = new List<Guid>();

        foreach (var _ in Enumerable.Range(0, 5))
        {
            var requested = await factory
                .CreateClientAs(Guid.NewGuid(), TenantId, [Roles.Member], [PermissionKeys.MembersRead])
                .PostAsJsonAsync(
                    $"/v1/pathshala/pathshalas/{pathshalaId}/enrollments",
                    new { childProfileId = Guid.NewGuid() });

            var enrolmentId = (await requested.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetGuid();

            await admin.PostAsJsonAsync(
                $"/v1/pathshala/enrollments/{enrolmentId}/placement",
                new { classId, place = true });

            enrolmentIds.Add(enrolmentId);
        }

        var teacher = factory.CreateClientAs(
            teacherId, TenantId, [Roles.PathshalaTeacher],
            [PermissionKeys.MembersRead, PermissionKeys.PathshalaAttendanceWrite]);

        var register = new
        {
            classDate = "2026-03-01",
            marks = enrolmentIds.Select(id => new { enrolmentId = id, status = "Present" }).ToArray(),
        };

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            teacher.PostAsJsonAsync($"/v1/pathshala/classes/{classId}/attendance", register)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
            "a teacher pressing Submit ten times has done nothing wrong");

        var rows = await factory.WithDbContextAsync(db =>
            db.Attendance.IgnoreQueryFilters().CountAsync(a => a.ClassId == classId));

        rows.Should().Be(5, "five students, one class day, one mark each");
    }

    // ---- The conversion link ------------------------------------------------

    [Fact]
    public async Task A_converted_child_can_read_their_own_records()
    {
        // The one promise the conversion flow makes about this service. Until
        // the event arrives, only the parent who asked for the place can read
        // these; after it, so can the person the child has become.
        var childProfileId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var studentUserId = Guid.NewGuid();

        var admin = factory.CreateClientAs(
            Guid.NewGuid(), TenantId, [Roles.SuperAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.PathshalaManage]);

        var created = await admin.PostAsJsonAsync("/v1/pathshala/pathshalas", new
        {
            name = "Conversion", address = (string?)null, contactPerson = (string?)null,
        });

        var pathshalaId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await admin.PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{pathshalaId}/sessions",
            new { label = "2026-27", startDate = "2026-03-01", endDate = "2027-02-28" });

        var requested = await factory
            .CreateClientAs(parentId, TenantId, [Roles.Member], [PermissionKeys.MembersRead])
            .PostAsJsonAsync(
                $"/v1/pathshala/pathshalas/{pathshalaId}/enrollments", new { childProfileId });

        var enrolmentId = (await requested.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var student = factory.CreateClientAs(
            studentUserId, TenantId, [Roles.Member], [PermissionKeys.MembersRead]);

        (await student.GetAsync($"/v1/pathshala/enrollments/{enrolmentId}/attendance"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "they have no account link yet");

        await DeliverConversionAsync(childProfileId, studentUserId);

        (await student.GetAsync($"/v1/pathshala/enrollments/{enrolmentId}/attendance"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delivering_the_conversion_twice_changes_nothing()
    {
        // Delivery is at least once, so the second copy is the ordinary case
        // and must not be reported as a failure - the consumer would retry it
        // five times and then log it as lost.
        var childProfileId = Guid.NewGuid();
        var studentUserId = Guid.NewGuid();

        var first = await DeliverConversionAsync(childProfileId, studentUserId);
        var second = await DeliverConversionAsync(childProfileId, studentUserId);

        first.Should().Be(0, "nothing is enrolled for this child");
        second.Should().Be(0);
    }

    private async Task<int> DeliverConversionAsync(Guid childProfileId, Guid studentUserId)
    {
        // The consumer is a thin loop around this command; the command is what
        // decides anything, so it is what the test exercises.
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var envelope = new IntegrationEventEnvelope(
            Guid.NewGuid(),
            TenantId,
            ConsumeIntegrationEventCommandHandler.ConversionCompletedTopic,
            "UserActivatedFromChildDomainEvent",
            JsonSerializer.Serialize(new
            {
                userId = studentUserId,
                tenantId = TenantId,
                childProfileId,
                mobileOrEmail = "aarav@example.com",
                occurredAt = DateTimeOffset.UtcNow,
            }),
            DateTimeOffset.UtcNow);

        var result = await sender.Send(new ConsumeIntegrationEventCommand(envelope));

        result.IsSuccess.Should().BeTrue();

        return result.Value;
    }
}
