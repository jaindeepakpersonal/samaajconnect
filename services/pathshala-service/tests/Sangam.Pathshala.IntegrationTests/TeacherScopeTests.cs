using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.Pathshala.Application.Security;
using Xunit;

namespace Sangam.Pathshala.IntegrationTests;

/// <summary>
/// A teacher sees the Pathshalas they teach at and no others. For a teacher
/// from outside the Samaaj - Pathshala.Teach, no Members.Read - that is not a
/// view they can step out of: the parent-facing reads refuse them outright.
/// A member who also teaches keeps the member's view, and asks for the staff
/// view when they want it.
/// </summary>
public sealed class TeacherScopeTests(PathshalaApiFactory factory)
    : IClassFixture<PathshalaApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid TeacherLogin = Guid.NewGuid();
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

    /// <summary>What a teacher from outside the Samaaj holds.</summary>
    private HttpClient Outsider() => factory.CreateClientAs(
        TeacherLogin, TenantId, [Roles.PathshalaTeacher],
        [PermissionKeys.PathshalaTeach, PermissionKeys.PathshalaAttendanceWrite, PermissionKeys.PathshalaExamsWrite]);

    /// <summary>The same login as a member who teaches: Members.Read too, from the Member role.</summary>
    private HttpClient MemberTeacher() => factory.CreateClientAs(
        TeacherLogin, TenantId, [Roles.Member, Roles.PathshalaTeacher],
        [PermissionKeys.MembersRead, PermissionKeys.PathshalaTeach, PermissionKeys.PathshalaAttendanceWrite,
         PermissionKeys.PathshalaExamsWrite]);

    /// <summary>Two Pathshalas; the teacher teaches a class at the first only.</summary>
    private async Task<(Guid Theirs, Guid Other, Guid ClassId)> TwoPathshalasAsync()
    {
        async Task<Guid> PathshalaAsync(string name) =>
            (await (await Platform().PostAsJsonAsync("/v1/pathshala/pathshalas", new { name }))
                .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var theirs = await PathshalaAsync("Shri Mahavir Jain Pathshala");
        var other = await PathshalaAsync("Shri Parshvanath Pathshala");

        var opened = await Admin().PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{theirs}/sessions",
            new { label = "2026-27", startDate = "2026-03-01", endDate = "2027-02-28" });
        var sessionId = (await opened.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessions").EnumerateArray().First().GetProperty("id").GetGuid();

        var classId = (await (await Admin().PostAsJsonAsync(
                $"/v1/pathshala/pathshalas/{theirs}/classes", new { sessionId, name = "Class 8", roomLabel = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var teacherId = (await (await Admin().PostAsJsonAsync($"/v1/pathshala/pathshalas/{theirs}/teachers", new
            {
                fullName = "Pt. Ramesh Shastri",
                phone = "+91 98290 11111",
                userId = TeacherLogin.ToString(),
            }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await Admin().PostAsJsonAsync($"/v1/pathshala/classes/{classId}/teachers", new { teacherId, assign = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return (theirs, other, classId);
    }

    private static IEnumerable<Guid> Ids(JsonElement list) =>
        list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid());

    [Fact]
    public async Task A_teacher_from_outside_the_Samaaj_sees_only_the_Pathshala_they_teach()
    {
        var (theirs, other, _) = await TwoPathshalasAsync();
        var teacher = Outsider();

        Ids(await teacher.GetFromJsonAsync<JsonElement>("/v1/pathshala/pathshalas"))
            .Should().Equal(theirs);

        (await teacher.GetAsync($"/v1/pathshala/pathshalas/{theirs}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await teacher.GetAsync($"/v1/pathshala/pathshalas/{other}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "not even the directory card a member would see");
    }

    [Fact]
    public async Task And_works_there_without_Members_Read()
    {
        var (theirs, other, classId) = await TwoPathshalasAsync();
        var teacher = Outsider();

        (await teacher.GetAsync($"/v1/pathshala/pathshalas/{theirs}/students")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await teacher.GetAsync($"/v1/pathshala/pathshalas/{theirs}/teachers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await teacher.GetAsync($"/v1/pathshala/pathshalas/{theirs}/progress")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await teacher.GetAsync($"/v1/pathshala/pathshalas/{theirs}/announcements")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await teacher.GetAsync($"/v1/pathshala/classes/{classId}/roll")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await teacher.GetAsync($"/v1/pathshala/pathshalas/{other}/students")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task But_is_refused_everything_a_parent_does()
    {
        var (theirs, _, _) = await TwoPathshalasAsync();
        var teacher = Outsider();

        (await teacher.GetAsync("/v1/pathshala/enrollments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await teacher.GetAsync("/v1/pathshala/announcements")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await teacher.PostAsJsonAsync($"/v1/pathshala/pathshalas/{theirs}/enrollments", new
            {
                childProfileId = Guid.NewGuid(),
                contactName = "Ravi Shah",
                contactPhone = "+91 98290 22222",
            }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_who_teaches_keeps_the_directory_and_asks_for_the_staff_view()
    {
        var (theirs, other, _) = await TwoPathshalasAsync();
        var teacher = MemberTeacher();

        Ids(await teacher.GetFromJsonAsync<JsonElement>("/v1/pathshala/pathshalas"))
            .Should().BeEquivalentTo([theirs, other], "a member reads the whole directory");

        Ids(await teacher.GetFromJsonAsync<JsonElement>("/v1/pathshala/pathshalas?staffed=true"))
            .Should().Equal(theirs);

        (await teacher.GetAsync($"/v1/pathshala/pathshalas/{other}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await teacher.GetAsync($"/v1/pathshala/pathshalas/{other}?asStaff=true"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await teacher.GetAsync($"/v1/pathshala/pathshalas/{theirs}?asStaff=true"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_administrator_s_staff_view_is_every_Pathshala_they_run()
    {
        var (theirs, other, _) = await TwoPathshalasAsync();

        Ids(await Admin().GetFromJsonAsync<JsonElement>("/v1/pathshala/pathshalas?staffed=true"))
            .Should().BeEquivalentTo([theirs, other]);
    }
}
