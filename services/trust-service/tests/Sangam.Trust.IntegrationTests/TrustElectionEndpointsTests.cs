using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sangam.Trust.IntegrationTests;

/// <summary>
/// A Trust election, end to end, through the endpoints - nominate, approve,
/// vote, close, publish - and what publishing actually does to the Trust's
/// own roster.
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

    private async Task AddMemberAsync(Guid trustId, Guid memberId, string? positionTitle = null) =>
        await Admin().PostAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/members",
            new { memberId = memberId.ToString(), positionTitle });

    private async Task<Guid> OpenElectionAsync(Guid trustId, string positionTitle = "Treasurer")
    {
        var response = await Admin().PostAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/elections",
            new
            {
                positionTitle,
                nominationStartAt = Start,
                nominationEndAt = Start.AddDays(7),
                votingStartAt = Start.AddDays(7),
                votingEndAt = Start.AddDays(14),
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task The_whole_path_elects_a_treasurer_and_installs_them_on_the_roster()
    {
        var trustId = await CreateTrustAsync();
        var candidateA = Guid.NewGuid();
        var candidateB = Guid.NewGuid();
        var voter = Guid.NewGuid();
        var previousHolder = Guid.NewGuid();

        await AddMemberAsync(trustId, candidateA);
        await AddMemberAsync(trustId, candidateB);
        await AddMemberAsync(trustId, voter);
        await AddMemberAsync(trustId, previousHolder, "Treasurer");

        var electionId = await OpenElectionAsync(trustId);

        (await Admin().PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/status", new { status = "NominationsOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var nominateA = await Member(voter).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/candidates", new { memberId = candidateA.ToString() });
        var candidateAId = (await nominateA.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("candidateId").GetGuid();

        var nominateB = await Member(voter).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/candidates", new { memberId = candidateB.ToString() });
        var candidateBId = (await nominateB.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("candidateId").GetGuid();

        await Admin().PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/candidates/{candidateAId}/decide", new { approve = true });
        await Admin().PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/candidates/{candidateBId}/decide", new { approve = true });

        factory.Clock.Set(Start.AddDays(7));

        (await Admin().PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/status", new { status = "VotingOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Candidate A cannot vote for themselves.
        (await Member(candidateA).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/votes", new { candidateId = candidateAId }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The voter picks A, and a repeat vote is reported as already cast.
        var firstVote = await Member(voter).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/votes", new { candidateId = candidateAId });
        (await firstVote.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accepted").GetBoolean().Should().BeTrue();

        var secondVote = await Member(voter).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/votes", new { candidateId = candidateBId });
        (await secondVote.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accepted").GetBoolean().Should().BeFalse();

        // Candidate B also votes for A - so A leads 2-0.
        await Member(candidateB).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/votes", new { candidateId = candidateAId });

        await Admin().PostAsJsonAsync($"/v1/trust/elections/{electionId}/status", new { status = "Closed" });

        var published = await Admin().PostAsJsonAsync($"/v1/trust/elections/{electionId}/publish", new { });

        published.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await published.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("winnerMemberId").GetGuid().Should().Be(candidateA);
        result.GetProperty("status").GetString().Should().Be("Published");

        var roster = await Admin().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/members");

        roster.EnumerateArray()
            .Single(m => m.GetProperty("memberId").GetGuid() == candidateA)
            .GetProperty("positionTitle").GetString().Should().Be("Treasurer");

        roster.EnumerateArray()
            .Single(m => m.GetProperty("memberId").GetGuid() == previousHolder)
            .GetProperty("positionTitle").ValueKind.Should().Be(JsonValueKind.Null,
                "a position has one holder - the winner takes it from whoever held it before");

        // Publishing twice changes nothing and does not reinstall the position.
        var republished = await Admin().PostAsJsonAsync($"/v1/trust/elections/{electionId}/publish", new { });
        republished.StatusCode.Should().Be(HttpStatusCode.OK);

        var rosterAgain = await Admin().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/members");

        rosterAgain.EnumerateArray()
            .Single(m => m.GetProperty("memberId").GetGuid() == candidateA)
            .GetProperty("positionTitle").GetString().Should().Be("Treasurer");
    }

    [Fact]
    public async Task A_non_member_cannot_be_nominated()
    {
        var trustId = await CreateTrustAsync();
        var voter = Guid.NewGuid();
        await AddMemberAsync(trustId, voter);

        var electionId = await OpenElectionAsync(trustId);

        await Admin().PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/status", new { status = "NominationsOpen" });

        var response = await Member(voter).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/candidates", new { memberId = Guid.NewGuid().ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_non_member_cannot_nominate()
    {
        var trustId = await CreateTrustAsync();
        var candidate = Guid.NewGuid();
        await AddMemberAsync(trustId, candidate);

        var electionId = await OpenElectionAsync(trustId);

        await Admin().PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/status", new { status = "NominationsOpen" });

        var response = await Member(Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/candidates", new { memberId = candidate.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Voting_is_refused_once_the_ballot_is_empty_and_nominations_are_closed()
    {
        var trustId = await CreateTrustAsync();
        var electionId = await OpenElectionAsync(trustId);

        await Admin().PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/status", new { status = "NominationsOpen" });

        var response = await Admin().PostAsJsonAsync(
            $"/v1/trust/elections/{electionId}/status", new { status = "VotingOpen" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
