using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.VolunteerGroups.Application.Security;
using Xunit;

namespace Sangam.VolunteerGroups.IntegrationTests;

/// <summary>
/// An Area Admin administers the groups of one locality - and remains a member
/// of the whole Samaaj. The locality bounds the first and never the second.
/// </summary>
public sealed class AreaAdminGroupTests(VolunteerGroupsApiFactory factory)
    : IClassFixture<VolunteerGroupsApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Hiran = Guid.NewGuid();
    private static readonly Guid Sector14 = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient SamaajAdmin() =>
        factory.CreateClientAs(
            Guid.NewGuid(),
            TenantId,
            [Roles.SamaajAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.VolunteerGroupsManage, PermissionKeys.VolunteerGroupsLead]);

    /// <summary>Member alongside AreaAdmin, as every real admin token carries.</summary>
    private HttpClient AreaAdmin(Guid locality) =>
        factory.CreateClientAs(
            Guid.NewGuid(),
            TenantId,
            [Roles.Member, Roles.AreaAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.VolunteerGroupsManage, PermissionKeys.VolunteerGroupsLead],
            locality);

    /// <summary>A president-less group, set up by the Samaaj Admin.</summary>
    private async Task<Guid> GroupAsync(string name, Guid? locality)
    {
        var response = await SamaajAdmin().PostAsJsonAsync("/v1/volunteer-groups/groups", new
        {
            name,
            description = (string?)null,
            focusArea = (string?)null,
            presidentMemberId = (string?)null,
            localityId = locality,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> StandDown(HttpClient client, Guid id) =>
        client.PatchAsJsonAsync($"/v1/volunteer-groups/groups/{id}/status", new { status = "Inactive" });

    [Fact]
    public async Task A_group_an_Area_Admin_sets_up_lands_in_their_own_locality()
    {
        var response = await AreaAdmin(Hiran).PostAsJsonAsync("/v1/volunteer-groups/groups", new
        {
            name = "Hiran Magri Seva",
            description = (string?)null,
            focusArea = (string?)null,
            presidentMemberId = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("localityId").GetGuid().Should().Be(Hiran);
    }

    [Fact]
    public async Task An_Area_Admin_cannot_set_up_a_group_in_another_locality()
    {
        var response = await AreaAdmin(Hiran).PostAsJsonAsync("/v1/volunteer-groups/groups", new
        {
            name = "Sector 14 Seva",
            description = (string?)null,
            focusArea = (string?)null,
            presidentMemberId = (string?)null,
            localityId = Sector14,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Area_Admin_administers_their_own_locality_s_groups_and_no_others()
    {
        var own = await GroupAsync("Hiran Magri Seva", Hiran);
        var elsewhere = await GroupAsync("Sector 14 Seva", Sector14);
        var samaajWide = await GroupAsync("Samaaj Yuva Mandal", null);

        (await StandDown(AreaAdmin(Hiran), own)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Not found rather than forbidden, the same answer a group in another
        // Samaaj gets.
        (await StandDown(AreaAdmin(Hiran), elsewhere)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A group serving no particular locality is the Samaaj Admin's alone.
        (await StandDown(AreaAdmin(Hiran), samaajWide)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Area_Admin_cannot_move_a_group_out_of_their_locality()
    {
        var own = await GroupAsync("Hiran Magri Seva", Hiran);

        var response = await AreaAdmin(Hiran).PatchAsJsonAsync($"/v1/volunteer-groups/groups/{own}", new
        {
            name = "Hiran Magri Seva",
            description = (string?)null,
            focusArea = (string?)null,
            localityId = Sector14,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Area_Admin_runs_a_president_less_group_only_in_their_locality()
    {
        var own = await GroupAsync("Hiran Magri Seva", Hiran);
        var elsewhere = await GroupAsync("Sector 14 Seva", Sector14);

        (await AreaAdmin(Hiran).GetAsync($"/v1/volunteer-groups/groups/{own}/applications"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AreaAdmin(Hiran).GetAsync($"/v1/volunteer-groups/groups/{elsewhere}/applications"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task As_a_member_an_Area_Admin_still_sees_every_group_in_the_Samaaj()
    {
        var own = await GroupAsync("Hiran Magri Seva", Hiran);
        var elsewhere = await GroupAsync("Sector 14 Seva", Sector14);

        var listed = (await AreaAdmin(Hiran).GetFromJsonAsync<JsonElement>("/v1/volunteer-groups/groups"))
            .EnumerateArray()
            .ToDictionary(g => g.GetProperty("id").GetGuid(), g => g.GetProperty("iCanLead").GetBoolean());

        listed.Should().ContainKey(elsewhere);
        listed[own].Should().BeTrue();
        listed[elsewhere].Should().BeFalse();
    }
}
