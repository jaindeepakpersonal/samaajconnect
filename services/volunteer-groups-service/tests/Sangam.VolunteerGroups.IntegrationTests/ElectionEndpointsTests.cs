using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.VolunteerGroups.Application.Security;
using Xunit;

namespace Sangam.VolunteerGroups.IntegrationTests;

/// <summary>
/// A volunteer-group election, end to end, through the gateway-shaped
/// endpoints - nominate, approve, vote, close, publish - and what publishing
/// actually does to the group's own roster.
/// </summary>
public sealed class ElectionEndpointsTests(VolunteerGroupsApiFactory factory)
    : IClassFixture<VolunteerGroupsApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PresidentId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid userId) =>
        factory.CreateClientAs(
            userId,
            TenantId,
            [Roles.Member],
            [PermissionKeys.MembersRead, PermissionKeys.VolunteerGroupsLead]);

    /// <summary>Acts as the group's own president - Manager()'s userId always matches CreateGroupAsync's.</summary>
    private HttpClient President() =>
        factory.CreateClientAs(
            PresidentId,
            TenantId,
            [Roles.SamaajAdmin],
            [
                PermissionKeys.MembersRead,
                PermissionKeys.VolunteerGroupsManage,
                PermissionKeys.VolunteerGroupsLead,
            ]);

    private async Task<Guid> CreateGroupAsync()
    {
        var response = await President().PostAsJsonAsync("/v1/volunteer-groups/groups", new
        {
            name = "Seva Group",
            description = "Food drives and blood donation camps.",
            focusArea = "Social Service",
            presidentMemberId = PresidentId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A fresh member, applied and accepted into the group.</summary>
    private async Task<Guid> AcceptedMemberAsync(Guid groupId)
    {
        var memberId = Guid.NewGuid();

        await Member(memberId).PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/applications", new { note = (string?)null });

        var queue = await President().GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{groupId}/applications");

        var applicationId = queue.EnumerateArray()
            .Single(a => a.GetProperty("memberId").GetGuid() == memberId)
            .GetProperty("id").GetGuid();

        await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/applications/{applicationId}/decide",
            new { accept = true });

        return memberId;
    }

    /// <summary>
    /// This service has no movable test clock (unlike celebrity-voting-
    /// service's own <c>TestClock</c>), so nominations and voting are windowed
    /// against the real clock: nominations open immediately and close a
    /// second and a half later, exactly when voting opens - a test walking
    /// the whole path waits out that gap with a real, short delay rather than
    /// widening the windows to overlap, which the validator refuses anyway
    /// (voting must start at or after nominations close).
    /// </summary>
    private async Task<Guid> OpenElectionAsync(Guid groupId, string positionTitle = "Secretary")
    {
        var now = DateTimeOffset.UtcNow;
        var nominationEnd = now.AddMilliseconds(1500);

        var response = await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/elections",
            new
            {
                positionTitle,
                nominationStartAt = now,
                nominationEndAt = nominationEnd,
                votingStartAt = nominationEnd,
                votingEndAt = nominationEnd.AddMinutes(10),
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task The_whole_path_elects_a_secretary_and_installs_them_on_the_roster()
    {
        var groupId = await CreateGroupAsync();
        var candidateA = await AcceptedMemberAsync(groupId);
        var candidateB = await AcceptedMemberAsync(groupId);
        var voter = await AcceptedMemberAsync(groupId);

        var electionId = await OpenElectionAsync(groupId);

        // Nominations open, and both members are put forward.
        (await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "NominationsOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var nominateA = await Member(voter).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates",
            new { memberId = candidateA.ToString() });
        var candidateAId = (await nominateA.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("candidateId").GetGuid();

        var nominateB = await Member(voter).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates",
            new { memberId = candidateB.ToString() });
        var candidateBId = (await nominateB.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("candidateId").GetGuid();

        // The president approves both onto the ballot.
        await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates/{candidateAId}/decide",
            new { approve = true });
        await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates/{candidateBId}/decide",
            new { approve = true });

        // Wait out the real-clock gap between the nomination window closing
        // and the voting window opening - see OpenElectionAsync's own remarks.
        await Task.Delay(1600);

        (await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "VotingOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Candidate A cannot vote for themselves.
        (await Member(candidateA).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/votes", new { candidateId = candidateAId }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The voter picks A, and a repeat vote is reported as already cast.
        var firstVote = await Member(voter).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/votes", new { candidateId = candidateAId });
        (await firstVote.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accepted").GetBoolean().Should().BeTrue();

        var secondVote = await Member(voter).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/votes", new { candidateId = candidateBId });
        (await secondVote.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accepted").GetBoolean().Should().BeFalse();

        // Candidate B also votes, for A - so A leads 2-0.
        await Member(candidateB).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/votes", new { candidateId = candidateAId });

        await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "Closed" });

        var published = await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/publish", new { });

        published.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await published.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("winnerMemberId").GetGuid().Should().Be(candidateA);
        result.GetProperty("status").GetString().Should().Be("Published");

        // And the roster itself reflects it.
        var detail = await President().GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{groupId}");

        detail.GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("memberId").GetGuid() == candidateA)
            .GetProperty("rolePosition").GetString().Should().Be("Secretary");
    }

    [Fact]
    public async Task A_non_member_cannot_be_nominated()
    {
        var groupId = await CreateGroupAsync();
        var voter = await AcceptedMemberAsync(groupId);
        var electionId = await OpenElectionAsync(groupId);

        await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "NominationsOpen" });

        var response = await Member(voter).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates",
            new { memberId = Guid.NewGuid().ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Voting_is_refused_once_the_ballot_is_empty_and_nominations_are_closed()
    {
        var groupId = await CreateGroupAsync();
        var electionId = await OpenElectionAsync(groupId);

        await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "NominationsOpen" });

        var response = await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "VotingOpen" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
