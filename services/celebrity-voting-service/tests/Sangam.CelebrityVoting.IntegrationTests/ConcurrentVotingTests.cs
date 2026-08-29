using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.CelebrityVoting.Application.Security;
using Xunit;

namespace Sangam.CelebrityVoting.IntegrationTests;

/// <summary>
/// The correctness requirement SERVICES.md singles out: no double voting, under
/// concurrency, on the platform's busiest write path.
/// </summary>
/// <remarks>
/// These are the tests the design exists for. Everything else about this
/// service could be proved against a substituted repository; this cannot,
/// because the thing being tested <i>is</i> a database index, and the only way
/// to show an index holding under concurrent inserts is to make concurrent
/// inserts against a real database.
///
/// DEVELOPMENT_PLAN.md asks for a load test of the vote endpoint. This is the
/// correctness half of that, and it is the half that can pass or fail: it
/// proves the invariant holds when requests actually race. Measuring throughput
/// under sustained load is a different exercise and belongs with the Phase 5
/// performance work, against a deployed environment rather than a
/// Testcontainer.
/// </remarks>
public sealed class ConcurrentVotingTests(CelebrityVotingApiFactory factory)
    : IClassFixture<CelebrityVotingApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        factory.Clock.Set(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

        return factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid userId) =>
        factory.CreateClientAs(userId, TenantId, [Roles.Member], [PermissionKeys.MembersRead]);

    private HttpClient Admin() =>
        factory.CreateClientAs(
            AdminId,
            TenantId,
            [Roles.SamaajAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.CelebrityVotingConfigure]);

    /// <summary>
    /// A campaign with voting open and two approved candidates.
    /// </summary>
    /// <remarks>
    /// Nominations and voting cannot be open at the same moment - the validator
    /// refuses a voting window that starts before nominations close, so that
    /// early voters and late voters see the same ballot. The clock is therefore
    /// moved between the two phases rather than the windows being fudged.
    /// </remarks>
    private async Task<(Guid CampaignId, Guid FirstCandidate, Guid SecondCandidate)> OpenVotingAsync()
    {
        var now = factory.Clock.UtcNow;

        var created = await Admin().PostAsJsonAsync("/v1/celebrity-voting/campaigns", new
        {
            title = "Celebrities of Samaaj 2026",
            description = (string?)null,
            nominationStartAt = now.AddMinutes(-10),
            nominationEndAt = now.AddHours(1),
            votingStartAt = now.AddHours(1),
            votingEndAt = now.AddDays(7),
            topN = 3,
            resultsVisibility = "Live",
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var campaignId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await Admin().PostAsJsonAsync(
            $"/v1/celebrity-voting/campaigns/{campaignId}/status",
            new { status = "NominationsOpen" });

        var candidates = new List<Guid>();

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var memberId = Guid.NewGuid();

            await Member(Guid.NewGuid()).PostAsJsonAsync(
                $"/v1/celebrity-voting/campaigns/{campaignId}/candidates",
                new { memberId, category = "Community service" });

            candidates.Add(memberId);
        }

        var detail = await Admin().GetFromJsonAsync<JsonElement>(
            $"/v1/celebrity-voting/campaigns/{campaignId}");

        var candidateIds = detail.GetProperty("candidates").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).ToList();

        foreach (var candidateId in candidateIds)
        {
            await Admin().PostAsJsonAsync(
                $"/v1/celebrity-voting/campaigns/{campaignId}/candidates/{candidateId}/decide",
                new { approve = true });
        }

        // Past the close of nominations and into the voting window.
        factory.Clock.Advance(TimeSpan.FromHours(2));

        (await Admin().PostAsJsonAsync(
                $"/v1/celebrity-voting/campaigns/{campaignId}/status",
                new { status = "VotingOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return (campaignId, candidateIds[0], candidateIds[1]);
    }

    private Task<HttpResponseMessage> VoteAsync(Guid voterId, Guid campaignId, Guid candidateId) =>
        Member(voterId).PostAsJsonAsync(
            $"/v1/celebrity-voting/campaigns/{campaignId}/votes", new { candidateId });

    // ---- The guarantee ----------------------------------------------------

    [Fact]
    public async Task Twenty_simultaneous_votes_from_one_member_produce_exactly_one_vote()
    {
        // The reason the unique index exists. A check-then-insert in the
        // handler passes for every one of these, because they all read before
        // any of them writes.
        var (campaignId, candidateId, _) = await OpenVotingAsync();
        var voterId = Guid.NewGuid();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => VoteAsync(voterId, campaignId, candidateId)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
            "a member pressing the button twenty times has done nothing wrong");

        var votes = await factory.WithDbContextAsync(db =>
            db.Votes.IgnoreQueryFilters().CountAsync(v => v.CampaignId == campaignId));

        votes.Should().Be(1);
    }

    [Fact]
    public async Task Exactly_one_of_those_requests_reports_that_it_was_accepted()
    {
        // Not just "one row landed" - the member is told the truth about which
        // of their presses counted.
        var (campaignId, candidateId, _) = await OpenVotingAsync();
        var voterId = Guid.NewGuid();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => VoteAsync(voterId, campaignId, candidateId)));

        var accepted = 0;

        foreach (var response in responses)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (body.GetProperty("accepted").GetBoolean())
            {
                accepted++;
            }
        }

        accepted.Should().Be(1);
    }

    [Fact]
    public async Task Racing_votes_for_two_different_candidates_still_leave_one_vote()
    {
        // The nastier version: the concurrent requests disagree about who to
        // vote for, so "already voted, nothing to do" is not enough - one of
        // them has to win outright.
        var (campaignId, first, second) = await OpenVotingAsync();
        var voterId = Guid.NewGuid();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(i =>
                VoteAsync(voterId, campaignId, i % 2 == 0 ? first : second)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        var votes = await factory.WithDbContextAsync(db =>
            db.Votes.IgnoreQueryFilters()
                .Where(v => v.CampaignId == campaignId)
                .ToListAsync());

        votes.Should().ContainSingle();
        new[] { first, second }.Should().Contain(votes[0].CandidateId);
    }

    [Fact]
    public async Task A_refused_vote_does_not_break_the_requests_around_it()
    {
        // A unique violation poisons the change tracker it happened on. If the
        // insert shared the request's context, one refused vote would fail
        // everything after it - so the vote is written on its own scope.
        var (campaignId, candidateId, _) = await OpenVotingAsync();
        var voterId = Guid.NewGuid();

        await VoteAsync(voterId, campaignId, candidateId);
        await VoteAsync(voterId, campaignId, candidateId);

        // Somebody else votes straight afterwards on the same service.
        var other = await VoteAsync(Guid.NewGuid(), campaignId, candidateId);

        other.StatusCode.Should().Be(HttpStatusCode.OK);
        (await other.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accepted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Many_members_voting_at_once_are_all_counted()
    {
        // The other side of the same coin: the index must not serialise
        // *different* voters into losing each other's votes.
        var (campaignId, candidateId, _) = await OpenVotingAsync();

        var voters = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToList();

        var responses = await Task.WhenAll(
            voters.Select(voterId => VoteAsync(voterId, campaignId, candidateId)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        var votes = await factory.WithDbContextAsync(db =>
            db.Votes.IgnoreQueryFilters().CountAsync(v => v.CampaignId == campaignId));

        votes.Should().Be(30);

        var tally = await Admin().GetFromJsonAsync<JsonElement>(
            $"/v1/celebrity-voting/campaigns/{campaignId}");

        tally.GetProperty("candidates").EnumerateArray()
            .First(c => c.GetProperty("id").GetGuid() == candidateId)
            .GetProperty("votes").GetInt32().Should().Be(30);
    }
}
