using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Pathshala.Application.Security;
using Xunit;

namespace Sangam.Pathshala.IntegrationTests;

/// <summary>
/// The admin panel's Pathshala tabs, end to end: teachers who need not be
/// members, the students with the contact their parents gave, the progress,
/// attendance and exam reports, the donation ledger, and announcements - and
/// who may and may not read each.
/// </summary>
public sealed class StaffTabsTests(PathshalaApiFactory factory)
    : IClassFixture<PathshalaApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid TeacherLogin = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync()
    {
        factory.Clock.Set(Start);

        return factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Platform() => factory.CreateClientAs(
        AdminId, TenantId, [Roles.SuperAdmin], [PermissionKeys.MembersRead, PermissionKeys.PathshalaManage]);

    private HttpClient Admin() => factory.CreateClientAs(
        AdminId, TenantId, [Roles.SamaajAdmin], [PermissionKeys.MembersRead, PermissionKeys.PathshalaManage]);

    /// <summary>
    /// A teacher account that is not a Samaaj member - the PathshalaTeacher role
    /// only, and so Pathshala.Teach rather than Members.Read.
    /// </summary>
    private HttpClient Teacher(Guid? login = null) => factory.CreateClientAs(
        login ?? TeacherLogin, TenantId, [Roles.PathshalaTeacher],
        [PermissionKeys.PathshalaTeach, PermissionKeys.PathshalaAttendanceWrite, PermissionKeys.PathshalaExamsWrite]);

    private HttpClient Member(Guid? id = null) => factory.CreateClientAs(
        id ?? ParentId, TenantId, [Roles.Member], [PermissionKeys.MembersRead]);

    private sealed record School(Guid PathshalaId, Guid ClassA, Guid ClassB, Guid TeacherId);

    /// <summary>A Pathshala with two classes, and a teacher with a login assigned to the first only.</summary>
    private async Task<School> ASchoolAsync()
    {
        var created = await Platform().PostAsJsonAsync("/v1/pathshala/pathshalas", new { name = "Shri Mahavir Jain Pathshala" });
        var pathshalaId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var opened = await Admin().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{pathshalaId}/sessions",
            new { label = "2026-27", startDate = "2026-03-01", endDate = "2027-02-28" });
        var sessionId = (await opened.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessions").EnumerateArray().First().GetProperty("id").GetGuid();

        async Task<Guid> ClassAsync(string name) =>
            (await (await Admin().PostAsJsonAsync(
                    $"/v1/pathshala/pathshalas/{pathshalaId}/classes", new { sessionId, name, roomLabel = (string?)null }))
                .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var classA = await ClassAsync("Class 8");
        var classB = await ClassAsync("Class 9");

        var teacher = await Admin().PostAsJsonAsync($"/v1/pathshala/pathshalas/{pathshalaId}/teachers", new
        {
            fullName = "Pt. Ramesh Shastri",
            phone = "+91 98290 11111",
            email = "ramesh@example.com",
            qualification = "Jain Darshan Shastri",
            userId = TeacherLogin.ToString(),
        });

        teacher.StatusCode.Should().Be(HttpStatusCode.Created);
        var teacherId = (await teacher.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await Admin().PostAsJsonAsync($"/v1/pathshala/classes/{classA}/teachers", new { teacherId, assign = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return new School(pathshalaId, classA, classB, teacherId);
    }

    private async Task<Guid> RequestAsync(Guid pathshalaId, Guid? parentId = null, string name = "Ravi Shah")
    {
        var requested = await Member(parentId).PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{pathshalaId}/enrollments",
            new { childProfileId = Guid.NewGuid(), contactName = name, contactPhone = "+91 98290 22222" });

        requested.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> PlacedAsync(Guid pathshalaId, Guid classId, Guid? parentId = null)
    {
        var enrolmentId = await RequestAsync(pathshalaId, parentId);

        (await Admin().PostAsJsonAsync(
                $"/v1/pathshala/enrollments/{enrolmentId}/placement", new { classId, place = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return enrolmentId;
    }

    // ---- Teachers ------------------------------------------------------------

    [Fact]
    public async Task A_teacher_who_is_not_a_member_is_a_record_the_admin_keeps_up_to_date()
    {
        var school = await ASchoolAsync();

        var noLogin = await Admin().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{school.PathshalaId}/teachers",
            new { fullName = "Smt. Kavita Jain", phone = "+91 98290 33333" });

        noLogin.StatusCode.Should().Be(HttpStatusCode.Created);
        var kavita = await noLogin.Content.ReadFromJsonAsync<JsonElement>();
        kavita.GetProperty("hasLogin").GetBoolean().Should().BeFalse();

        var teachers = await Admin().GetFromJsonAsync<JsonElement>($"/v1/pathshala/pathshalas/{school.PathshalaId}/teachers");
        teachers.EnumerateArray().Select(t => t.GetProperty("fullName").GetString())
            .Should().BeEquivalentTo(["Pt. Ramesh Shastri", "Smt. Kavita Jain"]);

        var corrected = await Admin().PatchAsJsonAsync(
            $"/v1/pathshala/pathshalas/{school.PathshalaId}/teachers/{kavita.GetProperty("id").GetGuid()}",
            new { fullName = "Smt. Kavita Jain", phone = "+91 98290 44444", qualification = "Acharya" });

        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await corrected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("phone").GetString()
            .Should().Be("+91 98290 44444");
    }

    [Fact]
    public async Task One_login_cannot_be_two_teachers_and_a_bad_phone_is_refused()
    {
        var school = await ASchoolAsync();
        var url = $"/v1/pathshala/pathshalas/{school.PathshalaId}/teachers";

        (await Admin().PostAsJsonAsync(url, new { fullName = "Again", userId = TeacherLogin.ToString() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await Admin().PostAsJsonAsync(url, new { fullName = "Bad", phone = "call me" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Removing_a_teacher_takes_away_their_class()
    {
        var school = await ASchoolAsync();

        (await Admin().DeleteAsync($"/v1/pathshala/pathshalas/{school.PathshalaId}/teachers/{school.TeacherId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // They no longer teach Class 8, so its roll is not theirs to read.
        (await Teacher().GetAsync($"/v1/pathshala/classes/{school.ClassA}/roll"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_reads_no_teacher_contact_details()
    {
        var school = await ASchoolAsync();

        (await Member().GetAsync($"/v1/pathshala/pathshalas/{school.PathshalaId}/teachers"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Students ------------------------------------------------------------

    [Fact]
    public async Task The_students_tab_shows_the_contact_each_parent_gave()
    {
        var school = await ASchoolAsync();
        await PlacedAsync(school.PathshalaId, school.ClassA);
        await PlacedAsync(school.PathshalaId, school.ClassB);
        await RequestAsync(school.PathshalaId, name: "Waiting Parent");

        var asAdmin = await Admin().GetFromJsonAsync<JsonElement>($"/v1/pathshala/pathshalas/{school.PathshalaId}/students");

        asAdmin.GetArrayLength().Should().Be(3);
        asAdmin.EnumerateArray().Should().OnlyContain(s => s.GetProperty("contactPhone").GetString() == "+91 98290 22222");
        asAdmin.EnumerateArray().Select(s => s.GetProperty("status").GetString())
            .Should().BeEquivalentTo(["Active", "Active", "Requested"]);

        // A teacher sees the children placed in their own class, and not the
        // waiting requests - those are the administrators' to decide.
        var asTeacher = await Teacher().GetFromJsonAsync<JsonElement>($"/v1/pathshala/pathshalas/{school.PathshalaId}/students");

        asTeacher.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("className").GetString().Should().Be("Class 8");

        (await Member().GetAsync($"/v1/pathshala/pathshalas/{school.PathshalaId}/students"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_request_without_a_phone_to_reach_the_parent_is_refused()
    {
        var school = await ASchoolAsync();

        (await Member().PostAsJsonAsync(
                $"/v1/pathshala/pathshalas/{school.PathshalaId}/enrollments",
                new { childProfileId = Guid.NewGuid(), contactName = "Ravi Shah", contactPhone = "" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Progress, attendance, exams -------------------------------------------

    [Fact]
    public async Task The_reports_add_up_the_register_and_the_marks()
    {
        var school = await ASchoolAsync();
        var diya = await PlacedAsync(school.PathshalaId, school.ClassA);
        var aarav = await PlacedAsync(school.PathshalaId, school.ClassA);

        (await Teacher().PostAsJsonAsync($"/v1/pathshala/classes/{school.ClassA}/attendance", new
        {
            classDate = "2026-03-01",
            marks = new[] { new { enrolmentId = diya, status = "Present" }, new { enrolmentId = aarav, status = "Absent" } },
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var exam = await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/classes/{school.ClassA}/exams", new { title = "Unit 1", examDate = "2026-03-01", maxScore = 20 });
        var examId = (await exam.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await Teacher().PostAsJsonAsync($"/v1/pathshala/exams/{examId}/results", new { enrolmentId = diya, score = 15, grade = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var progress = await Admin().GetFromJsonAsync<JsonElement>($"/v1/pathshala/pathshalas/{school.PathshalaId}/progress");
        var diyaRow = progress.EnumerateArray().Single(r => r.GetProperty("enrolmentId").GetGuid() == diya);

        diyaRow.GetProperty("attendancePercentage").GetInt32().Should().Be(100);
        diyaRow.GetProperty("examsSat").GetInt32().Should().Be(1);
        diyaRow.GetProperty("averageScorePercentage").GetDouble().Should().Be(75, "15 of 20, as a percentage");

        var attendance = await Admin().GetFromJsonAsync<JsonElement>($"/v1/pathshala/pathshalas/{school.PathshalaId}/attendance");
        var classA = attendance.EnumerateArray().Single(c => c.GetProperty("classId").GetGuid() == school.ClassA);

        classA.GetProperty("daysMarked").GetInt32().Should().Be(1);
        classA.GetProperty("percentage").GetInt32().Should().Be(50);
        classA.GetProperty("studentCount").GetInt32().Should().Be(2);

        var exams = await Admin().GetFromJsonAsync<JsonElement>($"/v1/pathshala/pathshalas/{school.PathshalaId}/exams");
        var row = exams.EnumerateArray().Single();

        row.GetProperty("resultsRecorded").GetInt32().Should().Be(1);
        row.GetProperty("studentCount").GetInt32().Should().Be(2);

        // The teacher of Class 8 sees Class 8 in the attendance overview, and
        // not Class 9.
        var asTeacher = await Teacher().GetFromJsonAsync<JsonElement>($"/v1/pathshala/pathshalas/{school.PathshalaId}/attendance");
        asTeacher.EnumerateArray().Should().ContainSingle();
    }

    // ---- Donations -----------------------------------------------------------

    [Fact]
    public async Task Donations_get_receipt_numbers_and_a_voided_one_leaves_the_total()
    {
        var school = await ASchoolAsync();
        var url = $"/v1/pathshala/pathshalas/{school.PathshalaId}/donations";

        var first = await Admin().PostAsJsonAsync(url, new
        {
            donorName = "Shri Mahendra Kothari", amount = 500_000L, donatedOn = "2026-03-01", mode = "Upi", purpose = "Books",
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        firstBody.GetProperty("receiptNumber").GetInt32().Should().Be(1);

        var second = await Admin().PostAsJsonAsync(url, new
        {
            donorName = "Anonymous", amount = 110_000L, donatedOn = "2026-03-01", mode = "cash",
        });
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptNumber").GetInt32().Should().Be(2);

        (await Admin().PostAsJsonAsync(
                $"/v1/pathshala/donations/{firstBody.GetProperty("id").GetGuid()}/void", new { reason = "Entered twice" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var ledger = await Admin().GetFromJsonAsync<JsonElement>(url);

        ledger.GetProperty("totalAmount").GetInt64().Should().Be(110_000L, "a voided entry is out of the total");
        ledger.GetProperty("donations").GetArrayLength().Should().Be(2, "and still in the ledger");

        (await Admin().PostAsJsonAsync(url, new { donorName = "X", amount = 100L, donatedOn = "2026-03-01", mode = "Barter" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Admin().PostAsJsonAsync(url, new { donorName = "X", amount = 0L, donatedOn = "2026-03-01", mode = "Cash" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Who gave how much is the administrators' to keep.
        (await Teacher().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_announced_donation_has_no_receipt_until_it_is_paid()
    {
        var school = await ASchoolAsync();
        var url = $"/v1/pathshala/pathshalas/{school.PathshalaId}/donations";

        var paid = await (await Admin().PostAsJsonAsync(url, new
        {
            donorName = "Shri Mahendra Kothari", amount = 500_000L, donatedOn = "2026-03-01", mode = "Upi",
        })).Content.ReadFromJsonAsync<JsonElement>();

        // Announced at the Paryushan gathering, to be paid later: no mode, no receipt.
        var announced = await Admin().PostAsJsonAsync(url, new
        {
            donorName = "Smt. Kavita Jain", amount = 210_000L, donatedOn = "2026-03-01", paid = false, purpose = "Books",
        });
        announced.StatusCode.Should().Be(HttpStatusCode.Created);
        var pledge = await announced.Content.ReadFromJsonAsync<JsonElement>();
        var pledgeId = pledge.GetProperty("id").GetGuid();

        pledge.GetProperty("status").GetString().Should().Be("Pledged");
        pledge.GetProperty("receiptNumber").ValueKind.Should().Be(JsonValueKind.Null, "a receipt means money received");
        pledge.GetProperty("mode").ValueKind.Should().Be(JsonValueKind.Null);

        var ledger = await Admin().GetFromJsonAsync<JsonElement>(url);
        ledger.GetProperty("totalAmount").GetInt64().Should().Be(500_000L, "only what has come in");
        ledger.GetProperty("outstandingAmount").GetInt64().Should().Be(210_000L);

        // Corrected while still a pledge: it stays one, and asks for no mode.
        (await Admin().PatchAsJsonAsync($"/v1/pathshala/donations/{pledgeId}", new
            {
                donorName = "Smt. Kavita Jain", amount = 250_000L, donatedOn = "2026-03-01", purpose = "Books",
            }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Paid before it was announced is not a date anybody means.
        (await Admin().PostAsJsonAsync($"/v1/pathshala/donations/{pledgeId}/paid", new { mode = "Cash", paidOn = "2026-02-27" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        factory.Clock.Advance(TimeSpan.FromDays(5));
        var markedPaid = await Admin().PostAsJsonAsync(
            $"/v1/pathshala/donations/{pledgeId}/paid", new { mode = "Cash", paidOn = "2026-03-06" });
        markedPaid.StatusCode.Should().Be(HttpStatusCode.OK);
        var nowPaid = await markedPaid.Content.ReadFromJsonAsync<JsonElement>();

        nowPaid.GetProperty("status").GetString().Should().Be("Paid");
        nowPaid.GetProperty("receiptNumber").GetInt32().Should().Be(
            paid.GetProperty("receiptNumber").GetInt32() + 1, "the next number, issued on payment");
        nowPaid.GetProperty("paidOn").GetString().Should().Be("2026-03-06");

        (await Admin().PostAsJsonAsync($"/v1/pathshala/donations/{pledgeId}/paid", new { mode = "Cash", paidOn = "2026-03-06" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "paid once");

        ledger = await Admin().GetFromJsonAsync<JsonElement>(url);
        ledger.GetProperty("totalAmount").GetInt64().Should().Be(750_000L);
        ledger.GetProperty("outstandingAmount").GetInt64().Should().Be(0L);

        // A paid entry corrected needs how and when it was paid.
        (await Admin().PatchAsJsonAsync($"/v1/pathshala/donations/{pledgeId}", new
            {
                donorName = "Smt. Kavita Jain", amount = 250_000L, donatedOn = "2026-03-01",
            }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deleting_needs_a_reason_once_a_receipt_was_issued_and_never_reuses_its_number()
    {
        var school = await ASchoolAsync();
        var url = $"/v1/pathshala/pathshalas/{school.PathshalaId}/donations";

        var paidId = (await (await Admin().PostAsJsonAsync(url, new
        {
            donorName = "Anonymous", amount = 110_000L, donatedOn = "2026-03-01", mode = "Cash",
        })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var pledgeId = (await (await Admin().PostAsJsonAsync(url, new
        {
            donorName = "Shri Rakesh Jain", amount = 50_000L, donatedOn = "2026-03-01", paid = false,
        })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Nobody holds a slip for an announcement, so no reason is needed.
        (await Admin().PostAsJsonAsync($"/v1/pathshala/donations/{pledgeId}/void", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Receipt 1 is on a slip somewhere: say why.
        (await Admin().PostAsJsonAsync($"/v1/pathshala/donations/{paidId}/void", new { }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Admin().PostAsJsonAsync($"/v1/pathshala/donations/{paidId}/void", new { reason = "Entered twice" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // A deleted entry is not edited or paid.
        (await Admin().PostAsJsonAsync($"/v1/pathshala/donations/{pledgeId}/paid", new { mode = "Cash", paidOn = "2026-03-01" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var ledger = await Admin().GetFromJsonAsync<JsonElement>(url);
        ledger.GetProperty("totalAmount").GetInt64().Should().Be(0L);
        ledger.GetProperty("outstandingAmount").GetInt64().Should().Be(0L);

        var next = await (await Admin().PostAsJsonAsync(url, new
        {
            donorName = "Shri Mahendra Kothari", amount = 100_000L, donatedOn = "2026-03-01", mode = "Upi",
        })).Content.ReadFromJsonAsync<JsonElement>();

        next.GetProperty("receiptNumber").GetInt32().Should().Be(2, "receipt 1 still names the deleted entry");
    }

    // ---- Announcements -------------------------------------------------------

    [Fact]
    public async Task A_teacher_messages_every_parent_and_only_ids_leave_the_service()
    {
        var school = await ASchoolAsync();
        var otherParent = Guid.NewGuid();
        await PlacedAsync(school.PathshalaId, school.ClassA);
        await RequestAsync(school.PathshalaId, otherParent);

        var sent = await Teacher().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{school.PathshalaId}/announcements",
            new { title = "Paryushan timings", body = "Classes start at 7 am this week." });

        sent.StatusCode.Should().Be(HttpStatusCode.OK);
        (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipientCount").GetInt32().Should().Be(2);

        var payloads = await factory.WithDbContextAsync(db => db.OutboxMessages
            .Where(m => m.Topic.StartsWith("pathshala.announcement."))
            .Select(m => new { m.Topic, m.Payload })
            .ToListAsync());

        payloads.Should().OnlyContain(
            m => !m.Payload.Contains("Paryushan"), "the text stays in this service; the events carry ids");
        payloads.Count(m => m.Topic == "pathshala.announcement.sent.v1").Should().Be(1);

        // One per parent, so each gets their own notification.
        var addressed = payloads.Where(m => m.Topic == "pathshala.announcement.addressed.v1").ToList();
        addressed.Should().HaveCount(2);
        addressed.Should().ContainSingle(m => m.Payload.Contains(otherParent.ToString()));

        var mine = await Member(otherParent).GetFromJsonAsync<JsonElement>("/v1/pathshala/announcements");
        mine.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("body").GetString().Should().Be("Classes start at 7 am this week.");

        // A member who is not staff here cannot send one.
        (await Member().PostAsJsonAsync(
                $"/v1/pathshala/pathshalas/{school.PathshalaId}/announcements", new { title = "Hi", body = "Hello" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And a parent with nothing at this Pathshala sees nothing from it.
        (await Member(Guid.NewGuid()).GetFromJsonAsync<JsonElement>("/v1/pathshala/announcements"))
            .GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task An_announcement_with_nobody_to_send_it_to_is_refused()
    {
        var school = await ASchoolAsync();

        (await Admin().PostAsJsonAsync(
                $"/v1/pathshala/pathshalas/{school.PathshalaId}/announcements", new { title = "Hi", body = "Hello" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
