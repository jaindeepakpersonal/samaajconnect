using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.Pathshala.Application.Security;
using Xunit;

namespace Sangam.Pathshala.IntegrationTests;

/// <summary>
/// An Area Admin runs the Pathshalas of one locality - sessions, classes, the
/// placement queue - and none elsewhere, while every Pathshala stays in the
/// directory they see as a member.
/// </summary>
public sealed class AreaAdminPathshalaTests(PathshalaApiFactory factory)
    : IClassFixture<PathshalaApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Hiran = Guid.NewGuid();
    private static readonly Guid Sector14 = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Platform() => factory.CreateClientAs(
        Guid.NewGuid(), TenantId, [Roles.SuperAdmin],
        [PermissionKeys.MembersRead, PermissionKeys.PathshalaManage]);

    private HttpClient AreaAdmin(Guid locality) => factory.CreateClientAs(
        Guid.NewGuid(), TenantId, [Roles.Member, Roles.AreaAdmin],
        [PermissionKeys.MembersRead, PermissionKeys.PathshalaManage], locality);

    private async Task<Guid> PathshalaAsync(string name, Guid? locality)
    {
        var created = await Platform().PostAsJsonAsync("/v1/pathshala/pathshalas", new
        {
            name,
            addressLine = (string?)null,
            localityId = locality?.ToString(),
            contactPerson = (string?)null,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> OpenSession(HttpClient client, Guid pathshalaId) =>
        client.PostAsJsonAsync(
            $"/v1/pathshala/pathshalas/{pathshalaId}/sessions",
            new { label = "2026-27", startDate = "2026-03-01", endDate = "2027-02-28" });

    [Fact]
    public async Task An_Area_Admin_runs_their_own_locality_s_Pathshala_and_no_other()
    {
        var own = await PathshalaAsync("Hiran Magri Pathshala", Hiran);
        var elsewhere = await PathshalaAsync("Sector 14 Pathshala", Sector14);
        var noLocality = await PathshalaAsync("Samaaj Pathshala", null);

        (await OpenSession(AreaAdmin(Hiran), own)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await OpenSession(AreaAdmin(Hiran), elsewhere)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OpenSession(AreaAdmin(Hiran), noLocality)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Area_Admin_reads_the_placement_queue_only_for_their_own_locality()
    {
        var own = await PathshalaAsync("Hiran Magri Pathshala", Hiran);
        var elsewhere = await PathshalaAsync("Sector 14 Pathshala", Sector14);

        (await AreaAdmin(Hiran).GetAsync($"/v1/pathshala/pathshalas/{own}/enrollments/requests"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AreaAdmin(Hiran).GetAsync($"/v1/pathshala/pathshalas/{elsewhere}/enrollments/requests"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_Samaaj_wide_attendance_figure_is_not_an_Area_Admin_s()
    {
        (await AreaAdmin(Hiran).GetAsync("/v1/pathshala/attendance-summary"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient SamaajAdmin() => factory.CreateClientAs(
        Guid.NewGuid(), TenantId, [Roles.SamaajAdmin],
        [PermissionKeys.MembersRead, PermissionKeys.PathshalaManage]);

    private static Task<HttpResponseMessage> Update(HttpClient client, Guid pathshalaId, Guid? locality) =>
        client.PatchAsJsonAsync($"/v1/pathshala/pathshalas/{pathshalaId}", new
        {
            name = "Corrected Pathshala",
            addressLine = "Near the upashray",
            localityId = locality?.ToString(),
            contactPerson = (string?)null,
        });

    [Fact]
    public async Task A_Samaaj_Admin_gives_a_Pathshala_created_without_a_locality_one_and_its_Area_Admin_can_then_run_it()
    {
        // The case this command exists for: a Pathshala from before localities
        // mattered was the Samaaj Admin's for good, with no route to change it.
        var legacy = await PathshalaAsync("Samaaj Pathshala", null);

        (await OpenSession(AreaAdmin(Hiran), legacy)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var updated = await Update(SamaajAdmin(), legacy, Hiran);

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("Corrected Pathshala");
        body.GetProperty("address").GetProperty("localityId").GetGuid().Should().Be(Hiran);

        (await OpenSession(AreaAdmin(Hiran), legacy)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_Area_Admin_corrects_their_own_Pathshala_but_cannot_move_it_or_touch_another()
    {
        var own = await PathshalaAsync("Hiran Magri Pathshala", Hiran);
        var elsewhere = await PathshalaAsync("Sector 14 Pathshala", Sector14);

        (await Update(AreaAdmin(Hiran), elsewhere, Sector14)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Update(AreaAdmin(Hiran), own, Sector14)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Blank is filled with theirs: clearing it would hand the Pathshala
        // to the Samaaj Admin.
        var blank = await Update(AreaAdmin(Hiran), own, null);

        blank.StatusCode.Should().Be(HttpStatusCode.OK);
        (await blank.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("address").GetProperty("localityId").GetGuid().Should().Be(Hiran);
    }

    [Fact]
    public async Task A_member_cannot_correct_a_Pathshala()
    {
        var own = await PathshalaAsync("Hiran Magri Pathshala", Hiran);
        var member = factory.CreateClientAs(
            Guid.NewGuid(), TenantId, [Roles.Member], [PermissionKeys.MembersRead]);

        (await Update(member, own, Hiran)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task As_a_member_an_Area_Admin_still_sees_every_Pathshala()
    {
        await PathshalaAsync("Hiran Magri Pathshala", Hiran);
        var elsewhere = await PathshalaAsync("Sector 14 Pathshala", Sector14);

        var listed = await AreaAdmin(Hiran).GetFromJsonAsync<JsonElement>("/v1/pathshala/pathshalas");

        listed.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Should().Contain(elsewhere);
    }
}
