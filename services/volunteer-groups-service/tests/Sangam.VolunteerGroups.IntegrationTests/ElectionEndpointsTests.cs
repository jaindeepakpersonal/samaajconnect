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
    private async Task<Guid> OpenElectionAsync(Guid groupId, string title = "Secretary", int seats = 1)
    {
        var response = await TryOpenElectionAsync(President(), groupId, await RoleAsync(groupId, title, seats));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> TryOpenElectionAsync(HttpClient client, Guid groupId, Guid roleId)
    {
        var now = DateTimeOffset.UtcNow;
        var nominationEnd = now.AddMilliseconds(1500);

        return client.PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/elections",
            new
            {
                roleId = roleId.ToString(),
                nominationStartAt = now,
                nominationEndAt = nominationEnd,
                votingStartAt = nominationEnd,
                votingEndAt = nominationEnd.AddMinutes(10),
            });
    }

    /// <summary>The group's role with this title, defining it with these seats first if it is not there.</summary>
    private async Task<Guid> RoleAsync(Guid groupId, string title, int seats = 1)
    {
        var detail = await President().GetFromJsonAsync<JsonElement>($"/v1/volunteer-groups/groups/{groupId}");

        var existing = detail.GetProperty("roles").EnumerateArray()
            .Where(r => r.GetProperty("title").GetString() == title)
            .Select(r => r.GetProperty("id").GetGuid())
            .ToList();

        if (existing.Count == 1)
        {
            return existing[0];
        }

        var defined = await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/roles", new { title, seats });

        defined.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await defined.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roles").EnumerateArray()
            .Single(r => r.GetProperty("title").GetString() == title)
            .GetProperty("id").GetGuid();
    }

    private async Task<Guid> NominateAndApproveAsync(Guid electionId, Guid nominator, Guid nominee)
    {
        var nominated = await Member(nominator).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates", new { memberId = nominee.ToString() });
        var candidateId = (await nominated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("candidateId").GetGuid();

        (await President().PostAsJsonAsync(
                $"/v1/volunteer-groups/elections/{electionId}/candidates/{candidateId}/decide",
                new { approve = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return candidateId;
    }

    private Task<HttpResponseMessage> MoveAsync(Guid electionId, string status) =>
        President().PostAsJsonAsync($"/v1/volunteer-groups/elections/{electionId}/status", new { status });

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
            $"/v1/volunteer-groups/elections/{electionId}/votes", new { candidateIds = new[] { candidateAId } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The voter picks A, and a repeat vote is reported as already cast.
        var firstVote = await Member(voter).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/votes", new { candidateIds = new[] { candidateAId } });
        (await firstVote.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accepted").GetBoolean().Should().BeTrue();

        var secondVote = await Member(voter).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/votes", new { candidateIds = new[] { candidateBId } });
        (await secondVote.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accepted").GetBoolean().Should().BeFalse();

        // Candidate B also votes, for A - so A leads 2-0.
        await Member(candidateB).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/votes", new { candidateIds = new[] { candidateAId } });

        await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "Closed" });

        var published = await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/publish", new { });

        published.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await published.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("winnerMemberIds").EnumerateArray().Select(w => w.GetGuid()).Should().Equal(candidateA);
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

    // ---- Reopening nominations that closed too early ---------------------------

    [Fact]
    public async Task Extending_the_nomination_window_lets_a_late_nomination_through()
    {
        var groupId = await CreateGroupAsync();
        var candidate = await AcceptedMemberAsync(groupId);
        var electionId = await OpenElectionAsync(groupId);

        await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "NominationsOpen" });

        // Wait past the original window's own close, the same real-clock delay
        // OpenElectionAsync's own remarks describe.
        await Task.Delay(1600);

        (await Member(candidate).PostAsJsonAsync(
                $"/v1/volunteer-groups/elections/{electionId}/candidates",
                new { memberId = candidate.ToString() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "nominations closed by the clock");

        var now = DateTimeOffset.UtcNow;
        var extended = now.AddSeconds(3);

        var update = await President().PatchAsJsonAsync($"/v1/volunteer-groups/elections/{electionId}", new
        {
            roleId = (await RoleAsync(groupId, "Secretary")).ToString(),
            nominationStartAt = now.AddMinutes(-1),
            nominationEndAt = extended,
            votingStartAt = extended,
            votingEndAt = extended.AddMinutes(10),
        });

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await update.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("status").GetString().Should().Be("NominationsOpen", "reopening moves no status");

        var afterExtending = await Member(candidate).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates",
            new { memberId = candidate.ToString() });

        afterExtending.StatusCode.Should().Be(HttpStatusCode.OK);
        (await afterExtending.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("nominated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Updating_is_refused_once_voting_has_opened()
    {
        var groupId = await CreateGroupAsync();
        var electionId = await OpenElectionAsync(groupId);

        await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "NominationsOpen" });

        var candidate = await AcceptedMemberAsync(groupId);

        var nominate = await Member(candidate).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates",
            new { memberId = candidate.ToString() });

        var candidateId = (await nominate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("candidateId").GetGuid();

        // The ballot needs at least one approved name before voting can open.
        await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates/{candidateId}/decide",
            new { approve = true });

        await Task.Delay(1600);

        (await President().PostAsJsonAsync(
                $"/v1/volunteer-groups/elections/{electionId}/status", new { status = "VotingOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var now = DateTimeOffset.UtcNow;

        var response = await President().PatchAsJsonAsync($"/v1/volunteer-groups/elections/{electionId}", new
        {
            roleId = (await RoleAsync(groupId, "Secretary")).ToString(),
            nominationStartAt = now.AddMinutes(-10),
            nominationEndAt = now.AddMinutes(-1),
            votingStartAt = now,
            votingEndAt = now.AddMinutes(10),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_member_who_is_not_the_president_cannot_correct_the_election()
    {
        var groupId = await CreateGroupAsync();
        var electionId = await OpenElectionAsync(groupId);
        var stranger = await AcceptedMemberAsync(groupId);

        var now = DateTimeOffset.UtcNow;

        var response = await Member(stranger).PatchAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}", new
            {
                roleId = (await RoleAsync(groupId, "Secretary")).ToString(),
                nominationStartAt = now,
                nominationEndAt = now.AddDays(7),
                votingStartAt = now.AddDays(7),
                votingEndAt = now.AddDays(14),
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Roles with more than one seat -----------------------------------------

    [Fact]
    public async Task A_two_seat_role_elects_the_top_two_and_each_voter_picks_up_to_two()
    {
        var groupId = await CreateGroupAsync();
        var (a, b, c) = (await AcceptedMemberAsync(groupId), await AcceptedMemberAsync(groupId),
            await AcceptedMemberAsync(groupId));
        var (voter1, voter2) = (await AcceptedMemberAsync(groupId), await AcceptedMemberAsync(groupId));

        var electionId = await OpenElectionAsync(groupId, "Coordinator", seats: 2);
        (await MoveAsync(electionId, "NominationsOpen")).StatusCode.Should().Be(HttpStatusCode.OK);

        var candidateA = await NominateAndApproveAsync(electionId, voter1, a);
        var candidateB = await NominateAndApproveAsync(electionId, voter1, b);
        var candidateC = await NominateAndApproveAsync(electionId, voter1, c);

        await Task.Delay(1600);
        (await MoveAsync(electionId, "VotingOpen")).StatusCode.Should().Be(HttpStatusCode.OK);

        var votes = $"/v1/volunteer-groups/elections/{electionId}/votes";

        // Three picks for two seats is refused, and writes nothing.
        (await Member(voter1).PostAsJsonAsync(votes, new { candidateIds = new[] { candidateA, candidateB, candidateC } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ballot = await Member(voter1).PostAsJsonAsync(votes, new { candidateIds = new[] { candidateA, candidateB } });
        (await ballot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accepted").GetBoolean().Should().BeTrue();

        // A ballot is final: coming back for another pick is a second ballot.
        var again = await Member(voter1).PostAsJsonAsync(votes, new { candidateIds = new[] { candidateC } });
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accepted").GetBoolean().Should().BeFalse();

        await Member(voter2).PostAsJsonAsync(votes, new { candidateIds = new[] { candidateA } });

        var mine = await Member(voter1).GetFromJsonAsync<JsonElement>($"/v1/volunteer-groups/elections/{electionId}");
        mine.GetProperty("election").GetProperty("myVoteCandidateIds").EnumerateArray().Select(v => v.GetGuid())
            .Should().BeEquivalentTo([candidateA, candidateB]);

        await MoveAsync(electionId, "Closed");
        var published = await President().PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/publish", new { });

        published.StatusCode.Should().Be(HttpStatusCode.OK);
        (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("winnerMemberIds")
            .EnumerateArray().Select(w => w.GetGuid()).Should().Equal(a, b);

        var roster = (await President().GetFromJsonAsync<JsonElement>($"/v1/volunteer-groups/groups/{groupId}"))
            .GetProperty("members").EnumerateArray()
            .ToDictionary(m => m.GetProperty("memberId").GetGuid(), m => m.GetProperty("rolePosition").GetString());

        roster[a].Should().Be("Coordinator");
        roster[b].Should().Be("Coordinator");
        roster[c].Should().Be("Volunteer");
    }

    [Fact]
    public async Task Volunteer_has_no_election()
    {
        var groupId = await CreateGroupAsync();

        var response = await TryOpenElectionAsync(President(), groupId, await RoleAsync(groupId, "Volunteer"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_role_has_one_election_running_at_a_time()
    {
        var groupId = await CreateGroupAsync();
        await OpenElectionAsync(groupId, "Treasurer");

        var second = await TryOpenElectionAsync(President(), groupId, await RoleAsync(groupId, "Treasurer"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_president_cannot_stand_for_another_role()
    {
        // They hold the President role, which moves only with the presidency.
        var groupId = await CreateGroupAsync();
        var voter = await AcceptedMemberAsync(groupId);
        var electionId = await OpenElectionAsync(groupId, "Secretary");
        await MoveAsync(electionId, "NominationsOpen");

        var response = await Member(voter).PostAsJsonAsync(
            $"/v1/volunteer-groups/elections/{electionId}/candidates", new { memberId = PresidentId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_administrator_who_is_not_the_president_runs_any_role_s_election()
    {
        // Admins as well as the president, at any time - not only the
        // President election of a group with none (changed 2026-09-26).
        var groupId = await CreateGroupAsync();
        var admin = factory.CreateClientAs(
            Guid.NewGuid(), TenantId, [Roles.SamaajAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.VolunteerGroupsManage, PermissionKeys.VolunteerGroupsLead]);

        var roleId = await RoleAsync(groupId, "Secretary");

        (await TryOpenElectionAsync(admin, groupId, roleId)).StatusCode.Should().Be(HttpStatusCode.Created);

        // And an ordinary member cannot.
        (await TryOpenElectionAsync(Member(Guid.NewGuid()), groupId, await RoleAsync(groupId, "Treasurer")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
