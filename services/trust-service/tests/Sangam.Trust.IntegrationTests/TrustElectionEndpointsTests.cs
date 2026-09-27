using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sangam.Trust.IntegrationTests;

/// <summary>
/// A Trust election, end to end, through the endpoints - define a role,
/// nominate, approve, vote, close, publish - and what publishing actually does
/// to the Trust's own roster.
/// </summary>
public sealed class TrustElectionEndpointsTests(TrustApiFactory factory)
    : IClassFixture<TrustApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync()
    {
        factory.Clock.Set(Start);
        return factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin() =>
        factory.CreateClientAs(Guid.NewGuid(), TenantId, ["SamaajAdmin"], ["Trust.Manage", "Members.Read"]);

    private HttpClient Member(Guid userId) =>
        factory.CreateClientAs(userId, TenantId, ["Member"], ["Members.Read"]);

    private async Task<Guid> CreateTrustAsync()
    {
        var response = await Admin().PostAsJsonAsync(
            "/v1/trust/trusts", new { localityId = Guid.NewGuid().ToString(), name = "Hiran Magri Trust" });

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> DefineRoleAsync(Guid trustId, string title, int seats)
    {
        var response = await Admin().PostAsJsonAsync($"/v1/trust/trusts/{trustId}/roles", new { title, seats });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task AddMemberAsync(Guid trustId, Guid memberId, Guid? roleId = null) =>
        (await Admin().PostAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/members",
            new { memberId = memberId.ToString(), roleId = roleId?.ToString() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    private async Task<HttpResponseMessage> OpenElectionAsync(Guid trustId, Guid roleId) =>
        await Admin().PostAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/elections",
            new
            {
                roleId,
                nominationStartAt = Start,
                nominationEndAt = Start.AddDays(7),
                votingStartAt = Start.AddDays(7),
                votingEndAt = Start.AddDays(14),
            });

    private async Task<Guid> OpenedElectionAsync(Guid trustId, Guid roleId)
    {
        var response = await OpenElectionAsync(trustId, roleId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> NominateAndApproveAsync(Guid electionId, Guid nominator, Guid memberId)
    {
        var nominated = await Member(nominator).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/candidates", new { memberId = memberId.ToString() });
        var candidateId = (await nominated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("candidateId").GetGuid();

        await Admin().PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/candidates/{candidateId}/decide", new { approve = true });

        return candidateId;
    }

    private async Task MoveAsync(Guid electionId, string status) =>
        (await Admin().PostAsJsonAsync($"/v1/trust/elections/{electionId}/status", new { status }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    private Task<HttpResponseMessage> VoteAsync(Guid electionId, Guid voter, params Guid[] candidateIds) =>
        Member(voter).PostAsJsonAsync($"/v1/trust/elections/{electionId}/votes", new { candidateIds });

    private async Task<Dictionary<Guid, string?>> RolesByMemberAsync(Guid trustId)
    {
        var roster = await Admin().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/members");

        return roster.EnumerateArray().ToDictionary(
            m => m.GetProperty("memberId").GetGuid(),
            m => m.GetProperty("positionTitle").GetString());
    }

    [Fact]
    public async Task The_whole_path_elects_two_secretaries_and_seats_them_on_the_roster()
    {
        var trustId = await CreateTrustAsync();
        var secretaries = await DefineRoleAsync(trustId, "Secretary", seats: 2);

        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (voter, previousHolder) = (Guid.NewGuid(), Guid.NewGuid());

        foreach (var member in new[] { a, b, c, voter })
        {
            await AddMemberAsync(trustId, member);
        }

        await AddMemberAsync(trustId, previousHolder, secretaries);

        var electionId = await OpenedElectionAsync(trustId, secretaries);

        await MoveAsync(electionId, "NominationsOpen");

        var candidateA = await NominateAndApproveAsync(electionId, voter, a);
        var candidateB = await NominateAndApproveAsync(electionId, voter, b);
        var candidateC = await NominateAndApproveAsync(electionId, voter, c);

        factory.Clock.Set(Start.AddDays(7));
        await MoveAsync(electionId, "VotingOpen");

        // Three picks for two seats is refused, and so is a self-vote.
        (await VoteAsync(electionId, voter, candidateA, candidateB, candidateC))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await VoteAsync(electionId, a, candidateA, candidateB))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The voter picks A and B; a second ballot is reported as already cast,
        // carrying the first.
        var first = await (await VoteAsync(electionId, voter, candidateA, candidateB))
            .Content.ReadFromJsonAsync<JsonElement>();
        first.GetProperty("accepted").GetBoolean().Should().BeTrue();

        var second = await (await VoteAsync(electionId, voter, candidateC))
            .Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("accepted").GetBoolean().Should().BeFalse();
        second.GetProperty("candidateIds").EnumerateArray().Select(x => x.GetGuid())
            .Should().Equal(candidateA, candidateB);

        // A: 3, C: 2, B: 1.
        await VoteAsync(electionId, b, candidateA, candidateC);
        await VoteAsync(electionId, c, candidateA);
        await VoteAsync(electionId, previousHolder, candidateC);

        var mine = await Member(voter).GetFromJsonAsync<JsonElement>($"/v1/trust/elections/{electionId}");
        mine.GetProperty("election").GetProperty("myVoteCandidateIds").EnumerateArray()
            .Select(x => x.GetGuid()).Should().Equal(candidateA, candidateB);

        await MoveAsync(electionId, "Closed");

        var published = await Admin().PostAsJsonAsync($"/v1/trust/elections/{electionId}/publish", new { });

        published.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await published.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("status").GetString().Should().Be("Published");
        result.GetProperty("positionTitle").GetString().Should().Be("Secretary");
        result.GetProperty("winnerMemberIds").EnumerateArray().Select(x => x.GetGuid())
            .Should().Equal([a, c], "the top two by tally, in ranked order");

        var roles = await RolesByMemberAsync(trustId);
        roles[a].Should().Be("Secretary");
        roles[c].Should().Be("Secretary");
        roles[b].Should().Be("Trustee");
        roles[previousHolder].Should().Be("Trustee", "an election replaces a role's holders");

        // Publishing twice changes nothing and does not seat anybody again.
        (await Admin().PostAsJsonAsync($"/v1/trust/elections/{electionId}/publish", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await RolesByMemberAsync(trustId)).Should().BeEquivalentTo(roles);
    }

    [Fact]
    public async Task Nobody_is_elected_to_Trustee()
    {
        var trustId = await CreateTrustAsync();
        var roles = await Admin().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/roles");
        var trustee = roles.EnumerateArray().Single().GetProperty("id").GetGuid();

        (await OpenElectionAsync(trustId, trustee)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_role_is_not_elected_twice_at_once()
    {
        var trustId = await CreateTrustAsync();
        var treasurer = await DefineRoleAsync(trustId, "Treasurer", seats: 1);

        await OpenedElectionAsync(trustId, treasurer);

        (await OpenElectionAsync(trustId, treasurer)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await Admin().DeleteAsync($"/v1/trust/trusts/{trustId}/roles/{treasurer}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "a role being elected is not removed");
    }

    [Fact]
    public async Task A_non_member_cannot_be_nominated()
    {
        var trustId = await CreateTrustAsync();
        var treasurer = await DefineRoleAsync(trustId, "Treasurer", seats: 1);
        var voter = Guid.NewGuid();
        await AddMemberAsync(trustId, voter);

        var electionId = await OpenedElectionAsync(trustId, treasurer);

        await MoveAsync(electionId, "NominationsOpen");

        var response = await Member(voter).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/candidates", new { memberId = Guid.NewGuid().ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_non_member_cannot_nominate()
    {
        var trustId = await CreateTrustAsync();
        var treasurer = await DefineRoleAsync(trustId, "Treasurer", seats: 1);
        var candidate = Guid.NewGuid();
        await AddMemberAsync(trustId, candidate);

        var electionId = await OpenedElectionAsync(trustId, treasurer);

        await MoveAsync(electionId, "NominationsOpen");

        var response = await Member(Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/candidates", new { memberId = candidate.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Voting_is_refused_once_the_ballot_is_empty_and_nominations_are_closed()
    {
        var trustId = await CreateTrustAsync();
        var treasurer = await DefineRoleAsync(trustId, "Treasurer", seats: 1);
        var electionId = await OpenedElectionAsync(trustId, treasurer);

        await MoveAsync(electionId, "NominationsOpen");

        var response = await Admin().PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/status", new { status = "VotingOpen" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
