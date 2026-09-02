using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Pathshala.Application.Security;
using Xunit;

namespace Sangam.Pathshala.IntegrationTests;

/// <summary>
/// The whole Pathshala, end to end: a school, a session, a class, a parent
/// asking for a place, staff placing the child, a register, an exam.
/// </summary>
public sealed class PathshalaFlowTests(PathshalaApiFactory factory)
    : IClassFixture<PathshalaApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid TeacherId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();

    /// <summary>A Sunday, so a class meeting on Sundays has met.</summary>
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync()
    {
        factory.Clock.Set(Start);

        return factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- Callers ----------------------------------------------------------

    /// <summary>A Super Admin, who alone may create the master record.</summary>
    private HttpClient Platform() => factory.CreateClientAs(
        AdminId, TenantId, [Roles.SuperAdmin],
        [PermissionKeys.MembersRead, PermissionKeys.PathshalaManage]);

    /// <summary>A Samaaj Admin: runs the Pathshala, cannot create one.</summary>
    private HttpClient Admin() => factory.CreateClientAs(
        AdminId, TenantId, [Roles.SamaajAdmin],
        [PermissionKeys.MembersRead, PermissionKeys.PathshalaManage]);

    private HttpClient Teacher(Guid? id = null) => factory.CreateClientAs(
        id ?? TeacherId, TenantId, [Roles.PathshalaTeacher],
        [
            PermissionKeys.MembersRead,
            PermissionKeys.PathshalaAttendanceWrite,
            PermissionKeys.PathshalaExamsWrite,
        ]);

    private HttpClient Parent(Guid? id = null) => factory.CreateClientAs(
        id ?? ParentId, TenantId, [Roles.Member], [PermissionKeys.MembersRead]);

    // ---- Setup ------------------------------------------------------------

    private sealed record Fixture(Guid PathshalaId, Guid SessionId, Guid ClassId);

    private async Task<Fixture> AClassAsync()
    {
        var created = await Platform().PostAsJsonAsync("/v1/pathshala/pathshalas", new
        {
            name = "Shri Mahavir Jain Pathshala",
            address = "Hiran Magri",
            contactPerson = "Smt. Kavita Jain",
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var pathshalaId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var opened = await Admin().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{pathshalaId}/sessions",
            new { label = "2026-27", startDate = "2026-03-01", endDate = "2027-02-28" });

        opened.StatusCode.Should().Be(HttpStatusCode.OK);

        var sessionId = (await opened.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessions").EnumerateArray().First().GetProperty("id").GetGuid();

        var classCreated = await Admin().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{pathshalaId}/classes",
            new { sessionId, name = "Class 8 - Jain Studies", roomLabel = "Room 2" });

        classCreated.StatusCode.Should().Be(HttpStatusCode.OK);

        var classId = (await classCreated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        (await Admin().PostAsJsonAsync(
                $"/v1/pathshala/classes/{classId}/teachers",
                new { teacherMemberId = TeacherId, assign = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return new Fixture(pathshalaId, sessionId, classId);
    }

    /// <summary>A child asked for and placed in the class.</summary>
    private async Task<Guid> APlacedStudentAsync(
        Fixture fixture, Guid? childProfileId = null, Guid? parentId = null)
    {
        var requested = await Parent(parentId).PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{fixture.PathshalaId}/enrollments",
            new { childProfileId = childProfileId ?? Guid.NewGuid() });

        requested.StatusCode.Should().Be(HttpStatusCode.OK);

        var enrolmentId = (await requested.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        (await Admin().PostAsJsonAsync(
                $"/v1/pathshala/enrollments/{enrolmentId}/placement",
                new { classId = fixture.ClassId, place = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return enrolmentId;
    }

    // ---- Who may create a Pathshala ---------------------------------------

    [Fact]
    public async Task Only_the_platform_creates_the_master_record()
    {
        // DATA-MODEL.md section 9 reserves this one act, so the command carries
        // the SuperAdmin role as well as the permission.
        var response = await Admin().PostAsJsonAsync("/v1/pathshala/pathshalas", new
        {
            name = "Ours", address = (string?)null, contactPerson = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task But_the_Samaaj_runs_it()
    {
        // The other half of the same decision. Withholding Pathshala.Manage from
        // Samaaj Admins would have reserved creation too, and left every
        // operation reachable by nobody but the platform operator.
        var fixture = await AClassAsync();

        fixture.ClassId.Should().NotBeEmpty();
    }

    // ---- Enrolment is two steps -------------------------------------------

    [Fact]
    public async Task A_request_is_not_yet_a_place()
    {
        var fixture = await AClassAsync();

        var requested = await Parent().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{fixture.PathshalaId}/enrollments",
            new { childProfileId = Guid.NewGuid() });

        var body = await requested.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("status").GetString().Should().Be("Requested");
        body.GetProperty("classId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Asking_twice_for_the_same_child_returns_the_one_request()
    {
        // The courtesy check in front of the unique index on
        // (PathshalaId, ChildProfileId). Two rows would put the child on two
        // rolls with two attendance records, neither of them right.
        var fixture = await AClassAsync();
        var childProfileId = Guid.NewGuid();

        var first = await Parent().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{fixture.PathshalaId}/enrollments", new { childProfileId });

        var second = await Parent().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{fixture.PathshalaId}/enrollments", new { childProfileId });

        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        (await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid().Should().Be(firstId);

        var rows = await factory.WithDbContextAsync(db =>
            db.Enrolments.IgnoreQueryFilters()
                .CountAsync(e => e.ChildProfileId == childProfileId));

        rows.Should().Be(1);
    }

    [Fact]
    public async Task A_parent_cannot_place_their_own_child()
    {
        // The whole point of the second step. Placing is what somebody who
        // knows the family does, and it is the only check this service can make
        // that the child is who the request says.
        var fixture = await AClassAsync();

        var requested = await Parent().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{fixture.PathshalaId}/enrollments",
            new { childProfileId = Guid.NewGuid() });

        var enrolmentId = (await requested.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        (await Parent().PostAsJsonAsync(
                $"/v1/pathshala/enrollments/{enrolmentId}/placement",
                new { classId = fixture.ClassId, place = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Pathshala_with_no_current_session_takes_no_enrolments()
    {
        var created = await Platform().PostAsJsonAsync("/v1/pathshala/pathshalas", new
        {
            name = "Not open yet", address = (string?)null, contactPerson = (string?)null,
        });

        var pathshalaId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        (await Parent().PostAsJsonAsync(
                $"/v1/pathshala/pathshalas/{pathshalaId}/enrollments",
                new { childProfileId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- Marking a register ------------------------------------------------

    [Fact]
    public async Task A_teacher_marks_the_register_and_the_student_sees_it()
    {
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        var marked = await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/attendance",
            new
            {
                classDate = "2026-03-01",
                marks = new[] { new { enrolmentId, status = "Present" } },
            });

        marked.StatusCode.Should().Be(HttpStatusCode.OK);
        (await marked.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("recorded").GetInt32().Should().Be(1);

        var attendance = await Parent().GetFromJsonAsync<JsonElement>(
            $"/v1/pathshala/enrollments/{enrolmentId}/attendance");

        attendance.GetProperty("present").GetInt32().Should().Be(1);
        attendance.GetProperty("percentage").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task Re_marking_corrects_rather_than_duplicates()
    {
        // A teacher changing Present to Excused after a parent explains is the
        // ordinary case, and must not add a second row.
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        var register = new
        {
            classDate = "2026-03-01",
            marks = new[] { new { enrolmentId, status = "Absent" } },
        };

        await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/attendance", register);

        var amendment = await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/attendance",
            new
            {
                classDate = "2026-03-01",
                marks = new[] { new { enrolmentId, status = "Excused" } },
            });

        var body = await amendment.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("recorded").GetInt32().Should().Be(0);
        body.GetProperty("amended").GetInt32().Should().Be(1);

        var rows = await factory.WithDbContextAsync(db =>
            db.Attendance.IgnoreQueryFilters().CountAsync(a => a.EnrolmentId == enrolmentId));

        rows.Should().Be(1);

        var attendance = await Parent().GetFromJsonAsync<JsonElement>(
            $"/v1/pathshala/enrollments/{enrolmentId}/attendance");

        attendance.GetProperty("excused").GetInt32().Should().Be(1);

        // Excused is not held against them, so the percentage has nothing to
        // divide by and says so rather than claiming zero.
        attendance.GetProperty("percentage").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_register_cannot_be_marked_before_the_class_has_met()
    {
        var fixture = await AClassAsync();
        await APlacedStudentAsync(fixture);

        var response = await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/attendance",
            new
            {
                classDate = "2026-06-01",
                marks = new[] { new { enrolmentId = Guid.NewGuid(), status = "Present" } },
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_teacher_of_another_class_cannot_mark_this_one()
    {
        // Holding Pathshala.Attendance.Write says teacher, not teacher of this
        // class. Answered as not-found rather than forbidden: a 403 would
        // confirm the class exists.
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        var response = await Teacher(Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/attendance",
            new
            {
                classDate = "2026-03-01",
                marks = new[] { new { enrolmentId, status = "Present" } },
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_withdrawn_student_is_skipped_rather_than_failing_the_register()
    {
        // A teacher working from a printed list should not have twenty-five
        // marks rejected because one child left last week.
        var fixture = await AClassAsync();
        var staying = await APlacedStudentAsync(fixture);
        var leaving = await APlacedStudentAsync(fixture);

        (await Admin().DeleteAsync($"/v1/pathshala/enrollments/{leaving}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var marked = await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/attendance",
            new
            {
                classDate = "2026-03-01",
                marks = new[]
                {
                    new { enrolmentId = staying, status = "Present" },
                    new { enrolmentId = leaving, status = "Present" },
                },
            });

        marked.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await marked.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("recorded").GetInt32().Should().Be(1);
        body.GetProperty("ignored").GetInt32().Should().Be(1);
    }

    // ---- Exams -------------------------------------------------------------

    [Fact]
    public async Task An_exam_moves_through_upcoming_awaiting_and_completed()
    {
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        var scheduled = await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/exams",
            new { title = "Ahimsa & Jain Philosophy", examDate = "2026-09-18", maxScore = 50 });

        var examId = (await scheduled.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        async Task<string> StatusAsync() =>
            (await Parent().GetFromJsonAsync<JsonElement>(
                $"/v1/pathshala/enrollments/{enrolmentId}/exams"))
            .EnumerateArray().Single().GetProperty("status").GetString()!;

        (await StatusAsync()).Should().Be("Upcoming");

        // Past the exam date, still unmarked. The wireframe shows two states;
        // this is the third, and calling it Completed with a blank result would
        // read as a zero.
        factory.Clock.Set(new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero));

        (await StatusAsync()).Should().Be("AwaitingResult");

        (await Teacher().PostAsJsonAsync(
                $"/v1/pathshala/exams/{examId}/results",
                new { enrolmentId, score = 44, grade = "A" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await StatusAsync()).Should().Be("Completed");
    }

    [Fact]
    public async Task A_score_above_the_maximum_is_refused()
    {
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        var scheduled = await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/exams",
            new { title = "Jain History", examDate = "2026-08-10", maxScore = 50 });

        var examId = (await scheduled.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        (await Teacher().PostAsJsonAsync(
                $"/v1/pathshala/exams/{examId}/results",
                new { enrolmentId, score = 51, grade = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Progress_averages_exams_as_percentages_not_as_raw_scores()
    {
        // An exam out of 20 and one out of 100 are not comparable marks.
        // Averaging the raw numbers would let the longer paper decide.
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        async Task MarkAsync(string title, int maxScore, int score)
        {
            var scheduled = await Teacher().PostAsJsonAsync(
                $"/v1/pathshala/classes/{fixture.ClassId}/exams",
                new { title, examDate = "2026-08-10", maxScore });

            var examId = (await scheduled.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetGuid();

            await Teacher().PostAsJsonAsync(
                $"/v1/pathshala/exams/{examId}/results",
                new { enrolmentId, score, grade = (string?)null });
        }

        await MarkAsync("Short paper", maxScore: 20, score: 10);    // 50%
        await MarkAsync("Long paper", maxScore: 100, score: 90);    // 90%

        var progress = await Parent().GetFromJsonAsync<JsonElement>(
            $"/v1/pathshala/enrollments/{enrolmentId}/progress");

        // Not (10 + 90) / 120 = 83%.
        progress.GetProperty("averageScorePercentage").GetDouble().Should().Be(70);
        progress.GetProperty("examsSat").GetInt32().Should().Be(2);
    }

    // ---- Who may read a child's records -------------------------------------

    [Fact]
    public async Task Another_parent_cannot_read_this_child_records()
    {
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        var stranger = Parent(Guid.NewGuid());

        foreach (var path in new[] { "my-class", "attendance", "exams", "progress" })
        {
            (await stranger.GetAsync($"/v1/pathshala/enrollments/{enrolmentId}/{path}"))
                .StatusCode.Should().Be(
                    HttpStatusCode.NotFound,
                    "these are records about somebody's child, and a 403 would confirm "
                    + "which enrolment ids are real");
        }
    }

    [Fact]
    public async Task The_class_teacher_can()
    {
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        (await Teacher().GetAsync($"/v1/pathshala/enrollments/{enrolmentId}/progress"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_teacher_of_another_class_cannot()
    {
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        (await Teacher(Guid.NewGuid())
                .GetAsync($"/v1/pathshala/enrollments/{enrolmentId}/progress"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_class_roll_is_not_readable_by_a_parent()
    {
        // A roll is a list of other people's children.
        var fixture = await AClassAsync();
        await APlacedStudentAsync(fixture);

        (await Parent().GetAsync($"/v1/pathshala/classes/{fixture.ClassId}/roll"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Teacher().GetAsync($"/v1/pathshala/classes/{fixture.ClassId}/roll"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unplaced_child_has_no_class_to_show()
    {
        // Distinct from "no such enrolment": the parent needs to be able to tell
        // waiting from refused.
        var fixture = await AClassAsync();

        var requested = await Parent().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{fixture.PathshalaId}/enrollments",
            new { childProfileId = Guid.NewGuid() });

        var enrolmentId = (await requested.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        (await Parent().GetAsync($"/v1/pathshala/enrollments/{enrolmentId}/my-class"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- Reading a register and a class's exams ----------------------------

    [Fact]
    public async Task The_register_can_be_read_back_so_amending_it_is_not_a_guess()
    {
        // The write path amends silently: a mark not re-sent stays as it was.
        // Without this read a teacher correcting one child re-enters the class
        // from memory, and a wrong recollection does not fail - it just leaves
        // the register wrong. This is the read that makes the amend path safe.
        var fixture = await AClassAsync();
        var present = await APlacedStudentAsync(fixture);
        var absent = await APlacedStudentAsync(fixture);

        await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/attendance",
            new
            {
                classDate = "2026-03-01",
                marks = new[]
                {
                    new { enrolmentId = present, status = "Present" },
                    new { enrolmentId = absent, status = "Absent" },
                },
            });

        var register = await Teacher().GetFromJsonAsync<JsonElement>(
            $"/v1/pathshala/classes/{fixture.ClassId}/register?date=2026-03-01");

        var marks = register.EnumerateArray()
            .ToDictionary(m => m.GetProperty("enrolmentId").GetGuid(),
                          m => m.GetProperty("status").GetString());

        marks.Should().HaveCount(2);
        marks[present].Should().Be("Present");
        marks[absent].Should().Be("Absent");
    }

    [Fact]
    public async Task A_register_answers_for_the_date_asked_for_and_no_other()
    {
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/attendance",
            new
            {
                classDate = "2026-03-01",
                marks = new[] { new { enrolmentId, status = "Present" } },
            });

        var other = await Teacher().GetFromJsonAsync<JsonElement>(
            $"/v1/pathshala/classes/{fixture.ClassId}/register?date=2026-03-08");

        // Empty, not 404. A day nobody has marked yet is a normal state of a
        // register, and a teacher opening next Sunday's should see a blank form
        // rather than an error.
        other.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_parent_cannot_read_the_whole_class_register()
    {
        // Their own child's attendance, yes - through the enrolment. The class's
        // is a record of other people's children.
        var fixture = await AClassAsync();
        var enrolmentId = await APlacedStudentAsync(fixture);

        await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/attendance",
            new
            {
                classDate = "2026-03-01",
                marks = new[] { new { enrolmentId, status = "Present" } },
            });

        (await Parent().GetAsync(
                $"/v1/pathshala/classes/{fixture.ClassId}/register?date=2026-03-01"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_teacher_of_another_class_cannot_read_this_register()
    {
        // Holding the attendance permission is necessary and not sufficient -
        // the same rule the roll follows.
        var fixture = await AClassAsync();

        (await Teacher(Guid.NewGuid()).GetAsync(
                $"/v1/pathshala/classes/{fixture.ClassId}/register?date=2026-03-01"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_class_lists_its_exams_with_the_marks_already_recorded()
    {
        // Scheduling answered with an id and nothing listed them again, so an
        // exam set last week could not be marked this week by any route the
        // platform offered.
        var fixture = await AClassAsync();
        var marked = await APlacedStudentAsync(fixture);
        var unmarked = await APlacedStudentAsync(fixture);

        var scheduled = await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/exams",
            new { title = "Half-yearly", examDate = "2026-09-06", maxScore = 50 });

        var examId = (await scheduled.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        (await Teacher().PostAsJsonAsync(
                $"/v1/pathshala/exams/{examId}/results",
                new { enrolmentId = marked, score = 41, grade = "A" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var exams = await Teacher().GetFromJsonAsync<JsonElement>(
            $"/v1/pathshala/classes/{fixture.ClassId}/exams");

        exams.GetArrayLength().Should().Be(1);

        var exam = exams.EnumerateArray().First();

        exam.GetProperty("id").GetGuid().Should().Be(examId);
        exam.GetProperty("title").GetString().Should().Be("Half-yearly");
        exam.GetProperty("maxScore").GetInt32().Should().Be(50);

        var results = exam.GetProperty("results").EnumerateArray().ToList();

        // Only the child who has a mark. Who is still unmarked is the roll's
        // answer, not the exam's - and a teacher entering results needs to know
        // which of the two this is, because re-recording amends silently.
        results.Should().HaveCount(1);
        results[0].GetProperty("enrolmentId").GetGuid().Should().Be(marked);
        results[0].GetProperty("score").GetInt32().Should().Be(41);
        results[0].GetProperty("grade").GetString().Should().Be("A");

        results.Should().NotContain(r => r.GetProperty("enrolmentId").GetGuid() == unmarked);
    }

    [Fact]
    public async Task One_class_s_exams_do_not_leak_into_another_s()
    {
        var fixture = await AClassAsync();

        var otherCreated = await Admin().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{fixture.PathshalaId}/classes",
            new { sessionId = fixture.SessionId, name = "Class 9", roomLabel = (string?)null });

        var otherClassId = (await otherCreated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await Admin().PostAsJsonAsync(
            $"/v1/pathshala/classes/{fixture.ClassId}/exams",
            new { title = "Class 8 half-yearly", examDate = "2026-09-06", maxScore = 50 });

        var exams = await Admin().GetFromJsonAsync<JsonElement>(
            $"/v1/pathshala/classes/{otherClassId}/exams");

        exams.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_parent_cannot_list_a_class_s_exam_marks()
    {
        var fixture = await AClassAsync();

        (await Parent().GetAsync($"/v1/pathshala/classes/{fixture.ClassId}/exams"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
