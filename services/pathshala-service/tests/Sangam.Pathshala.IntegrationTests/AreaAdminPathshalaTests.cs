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

    [Fact]
    public async Task As_a_member_an_Area_Admin_still_sees_every_Pathshala()
    {
        await PathshalaAsync("Hiran Magri Pathshala", Hiran);
        var elsewhere = await PathshalaAsync("Sector 14 Pathshala", Sector14);

        var listed = await AreaAdmin(Hiran).GetFromJsonAsync<JsonElement>("/v1/pathshala/pathshalas");

        listed.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Should().Contain(elsewhere);
    }
}
