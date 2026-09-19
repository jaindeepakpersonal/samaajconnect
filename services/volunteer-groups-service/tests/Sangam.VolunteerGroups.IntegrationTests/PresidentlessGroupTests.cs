using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.VolunteerGroups.Application.Security;
using Xunit;

namespace Sangam.VolunteerGroups.IntegrationTests;

/// <summary>
/// A group created without a president, and the two ways it gets one: a Samaaj
/// admin choosing an existing member, or a "President" election. While it has
/// none, an admin - and only an admin - may accept joiners and run that one
/// election; the moment a president exists those powers stop.
/// </summary>
public sealed class PresidentlessGroupTests(VolunteerGroupsApiFactory factory)
    : IClassFixture<VolunteerGroupsApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid userId) =>
        factory.CreateClientAs(
            userId,
            TenantId,
            [Roles.Member],
            [PermissionKeys.MembersRead, PermissionKeys.VolunteerGroupsLead]);

    private HttpClient Admin() =>
        factory.CreateClientAs(
            AdminId,
            TenantId,
            [Roles.SamaajAdmin],
            [
                PermissionKeys.MembersRead,
                PermissionKeys.VolunteerGroupsManage,
                PermissionKeys.VolunteerGroupsLead,
            ]);

    private async Task<Guid> CreateGroupWithoutPresidentAsync()
    {
        var response = await Admin().PostAsJsonAsync("/v1/volunteer-groups/groups", new
        {
            name = "Seva Group " + Guid.NewGuid().ToString("N")[..6],
            description = "Food drives.",
            focusArea = "Social Service",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A fresh member who applies and is accepted by the admin.</summary>
    private async Task<Guid> AdminAcceptedMemberAsync(Guid groupId)
    {
        var memberId = Guid.NewGuid();

        await Member(memberId).PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/applications", new { note = (string?)null });

        var queue = await Admin().GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{groupId}/applications");

        var applicationId = queue.EnumerateArray()
            .Single(a => a.GetProperty("memberId").GetGuid() == memberId)
            .GetProperty("id").GetGuid();

        (await Admin().PostAsJsonAsync(
                $"/v1/volunteer-groups/groups/{groupId}/applications/{applicationId}/decide",
                new { accept = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return memberId;
    }

    private async Task<Guid> OpenElectionAsync(Guid groupId, string positionTitle)
    {
        var now = DateTimeOffset.UtcNow;
        var nominationEnd = now.AddMilliseconds(1500);

        var response = await Admin().PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/elections",
            new
            {
                positionTitle,
                nominationStartAt = now,
                nominationEndAt = nominationEnd,
                votingStartAt = nominationEnd,
                votingEndAt = nominationEnd.AddMinutes(10),
            });

        return response.StatusCode == HttpStatusCode.Created
            ? (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()
            : Guid.Empty;
    }

    [Fact]
    public async Task A_group_can_be_created_with_no_president_and_says_so()
    {
        var groupId = await CreateGroupWithoutPresidentAsync();

        var detail = await Admin().GetFromJsonAsync<JsonElement>($"/v1/volunteer-groups/groups/{groupId}");
        var group = detail.GetProperty("group");

        group.GetProperty("presidentMemberId").ValueKind.Should().Be(JsonValueKind.Null);
        group.GetProperty("iCanLead").GetBoolean().Should().BeTrue("an admin runs a group that has no president");
        detail.GetProperty("members").GetArrayLength().Should().Be(0, "there is nobody to seed it with");

        var asMember = await Member(Guid.NewGuid()).GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{groupId}");
        asMember.GetProperty("group").GetProperty("iCanLead").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_malformed_president_id_is_a_validation_error_not_a_crash()
    {
        var response = await Admin().PostAsJsonAsync("/v1/volunteer-groups/groups", new
        {
            name = "Bad id group",
            presidentMemberId = "not-a-guid",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_ordinary_member_cannot_decide_applications_on_a_group_with_no_president()
    {
        var groupId = await CreateGroupWithoutPresidentAsync();
        var applicant = Guid.NewGuid();
        await Member(applicant).PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/applications", new { note = (string?)null });

        var response = await Member(Guid.NewGuid()).GetAsync($"/v1/volunteer-groups/groups/{groupId}/applications");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_admin_can_pick_an_existing_member_as_the_first_president()
    {
        var groupId = await CreateGroupWithoutPresidentAsync();
        var member = await AdminAcceptedMemberAsync(groupId);

        var response = await Admin().PatchAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/president", new { newPresidentMemberId = member });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("presidentMemberId").GetGuid().Should().Be(member);
    }

    [Fact]
    public async Task Once_a_president_exists_the_admin_stops_running_the_group()
    {
        var groupId = await CreateGroupWithoutPresidentAsync();
        var member = await AdminAcceptedMemberAsync(groupId);

        await Admin().PatchAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/president", new { newPresidentMemberId = member });

        // The queue is now the president's business alone, admin or not.
        (await Admin().GetAsync($"/v1/volunteer-groups/groups/{groupId}/applications"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And an election, including for President, is now theirs to open.
        (await OpenElectionAsync(groupId, "President")).Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task An_admin_may_run_only_the_president_election_while_there_is_no_president()
    {
        var groupId = await CreateGroupWithoutPresidentAsync();

        (await OpenElectionAsync(groupId, "Secretary")).Should().Be(Guid.Empty);
        (await OpenElectionAsync(groupId, "president")).Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_president_election_installs_the_winner_as_the_real_president()
    {
        var groupId = await CreateGroupWithoutPresidentAsync();
        var candidate = await AdminAcceptedMemberAsync(groupId);
        var voter = await AdminAcceptedMemberAsync(groupId);

        var electionId = await OpenElectionAsync(groupId, "President");
        electionId.Should().NotBe(Guid.Empty);

        (await Admin().PostAsJsonAsync(
                $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "NominationsOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var nominated = await Member(voter).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates",
            new { memberId = candidate.ToString() });
        var candidateId = (await nominated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("candidateId").GetGuid();

        (await Admin().PostAsJsonAsync(
                $"/v1/volunteer-groups/elections/{electionId}/candidates/{candidateId}/decide",
                new { approve = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(1600);

        (await Admin().PostAsJsonAsync(
                $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "VotingOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await Member(voter).PostAsJsonAsync(
                $"/v1/volunteer-groups/elections/{electionId}/votes", new { candidateId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await Admin().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "Closed" });

        (await Admin().PostAsJsonAsync($"/v1/volunteer-groups/elections/{electionId}/publish", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await Member(candidate).GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{groupId}");
        var group = detail.GetProperty("group");

        group.GetProperty("presidentMemberId").GetGuid().Should().Be(candidate);
        group.GetProperty("iAmThePresident").GetBoolean().Should().BeTrue(
            "winning the election makes them president, not just someone labelled one");

        // The admin's interim powers ended with the election.
        (await Admin().GetAsync($"/v1/volunteer-groups/groups/{groupId}/applications"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
