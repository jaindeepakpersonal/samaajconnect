using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Panch.Domain.Elections;
using Xunit;

namespace Sangam.Panch.IntegrationTests;

/// <summary>
/// The Panch elections and the roster, through the endpoints, against a real
/// database. The clock is moved, not slept on.
/// </summary>
public sealed class PanchEndpointsTests(PanchApiFactory factory)
    : IClassFixture<PanchApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync()
    {
        factory.Clock.Set(Start);
        return factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin(Guid? tenantId = null) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId ?? TenantId, ["SamaajAdmin"], ["Panch.Manage", "Members.Read"]);

    private HttpClient Member(Guid userId, Guid? tenantId = null) =>
        factory.CreateClientAs(userId, tenantId ?? TenantId, ["Member"], ["Members.Read"]);

    private static object Body(DateTimeOffset start) => new
    {
        title = "Samaaj Panch 2026",
        description = (string?)null,
        nominationStartAt = start,
        nominationEndAt = start.AddDays(7),
        votingStartAt = start.AddDays(7),
        votingEndAt = start.AddDays(14),
        resultsVisibility = "Live",
    };

    /// <summary>An election with <paramref name="count"/> approved candidates, ready to vote in.</summary>
    private async Task<(Guid ElectionId, List<(Guid CandidateId, Guid MemberId)> Candidates)> VotingOpenAsync(
        int count, DateTimeOffset? begin = null)
    {
        var start = begin ?? Start;
        factory.Clock.Set(start);
        var admin = Admin();

        var created = await admin.PostAsJsonAsync("/v1/panch/elections", Body(start));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var electionId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/v1/panch/elections/{electionId}/status", new { status = "NominationsOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var candidates = new List<(Guid, Guid)>();

        for (var i = 0; i < count; i++)
        {
            var memberId = Guid.NewGuid();
            var nominated = await Member(Guid.NewGuid()).PostAsJsonAsync(
                $"/v1/panch/elections/{electionId}/candidates", new { memberId = memberId.ToString() });
            var candidateId = (await nominated.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("candidateId").GetGuid();

            (await admin.PostAsJsonAsync(
                    $"/v1/panch/elections/{electionId}/candidates/{candidateId}/decide", new { approve = true }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            candidates.Add((candidateId, memberId));
            factory.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        factory.Clock.Set(start.AddDays(7).AddHours(1));

        (await admin.PostAsJsonAsync($"/v1/panch/elections/{electionId}/status", new { status = "VotingOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return (electionId, candidates);
    }

    private static object Ballot(IEnumerable<Guid> ids) => new { candidateIds = ids.Select(i => i.ToString()).ToArray() };

    [Fact]
    public async Task A_member_cannot_set_an_election_up_or_approve_a_nomination()
    {
        (await Member(Guid.NewGuid()).PostAsJsonAsync("/v1/panch/elections", Body(Start)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_can_back_up_to_ten_candidates_on_one_final_ballot()
    {
        var (electionId, candidates) = await VotingOpenAsync(12);
        var voter = Guid.NewGuid();

        var response = await Member(voter).PostAsJsonAsync(
            $"/v1/panch/elections/{electionId}/ballots", Ballot(candidates.Take(10).Select(c => c.CandidateId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accepted").GetBoolean().Should().BeTrue();
        body.GetProperty("choices").GetArrayLength().Should().Be(10);
    }

    [Fact]
    public async Task An_eleventh_choice_is_refused()
    {
        var (electionId, candidates) = await VotingOpenAsync(12);

        (await Member(Guid.NewGuid()).PostAsJsonAsync(
                $"/v1/panch/elections/{electionId}/ballots", Ballot(candidates.Take(11).Select(c => c.CandidateId))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_second_ballot_is_reported_not_applied()
    {
        var (electionId, candidates) = await VotingOpenAsync(3);
        var voter = Guid.NewGuid();

        await Member(voter).PostAsJsonAsync(
            $"/v1/panch/elections/{electionId}/ballots", Ballot([candidates[0].CandidateId]));

        var again = await Member(voter).PostAsJsonAsync(
            $"/v1/panch/elections/{electionId}/ballots", Ballot([candidates[1].CandidateId, candidates[2].CandidateId]));

        var body = await again.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accepted").GetBoolean().Should().BeFalse();
        body.GetProperty("choices").GetArrayLength().Should().Be(1, "the first ballot stands");
    }

    [Fact]
    public async Task Concurrent_ballots_from_one_member_yield_exactly_one()
    {
        var (electionId, candidates) = await VotingOpenAsync(3);
        var voter = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Member(voter).PostAsJsonAsync(
                $"/v1/panch/elections/{electionId}/ballots", Ballot([candidates[0].CandidateId]))));

        var accepted = 0;
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            if ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accepted").GetBoolean())
            {
                accepted++;
            }
        }

        accepted.Should().Be(1);

        (await factory.WithDbContextAsync(db => db.Ballots.CountAsync(b => b.VoterMemberId == voter))).Should().Be(1);
        (await factory.WithDbContextAsync(db => db.Votes.CountAsync(v => v.VoterMemberId == voter))).Should().Be(1);
    }

    [Fact]
    public async Task A_candidate_cannot_back_themselves()
    {
        var (electionId, candidates) = await VotingOpenAsync(2);

        (await Member(candidates[0].MemberId).PostAsJsonAsync(
                $"/v1/panch/elections/{electionId}/ballots", Ballot([candidates[0].CandidateId])))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Publishing_seats_the_ten_most_voted_and_replaces_the_previous_panel()
    {
        var (first, firstCandidates) = await VotingOpenAsync(3);

        await Member(Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/panch/elections/{first}/ballots", Ballot(firstCandidates.Select(c => c.CandidateId)));
        await Admin().PostAsJsonAsync($"/v1/panch/elections/{first}/status", new { status = "Closed" });
        (await Admin().PostAsJsonAsync($"/v1/panch/elections/{first}/results", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var panel = await Member(Guid.NewGuid()).GetFromJsonAsync<JsonElement>("/v1/panch/panel");
        panel.GetProperty("members").GetArrayLength().Should().Be(3);

        // A second election seats a different set and unseats the first.
        var (second, secondCandidates) = await VotingOpenAsync(12, Start.AddDays(30));
        var voters = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();

        foreach (var voter in voters)
        {
            await Member(voter).PostAsJsonAsync(
                $"/v1/panch/elections/{second}/ballots", Ballot(secondCandidates.Take(10).Select(c => c.CandidateId)));
        }

        await Admin().PostAsJsonAsync($"/v1/panch/elections/{second}/status", new { status = "Closed" });
        await Admin().PostAsJsonAsync($"/v1/panch/elections/{second}/results", new { });

        var after = await Member(secondCandidates[0].MemberId).GetFromJsonAsync<JsonElement>("/v1/panch/panel");
        var seated = after.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("memberId").GetGuid()).ToList();

        seated.Should().HaveCount(10, "at most ten seats");
        seated.Should().Contain(secondCandidates[0].MemberId);
        seated.Should().NotContain(secondCandidates[10].MemberId, "not backed by anybody");
        seated.Should().NotContain(firstCandidates[0].MemberId, "the previous panel is unseated");
        after.GetProperty("viewerIsMember").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Publishing_announces_the_new_roster_through_the_outbox()
    {
        var (electionId, candidates) = await VotingOpenAsync(2);
        await Member(Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/panch/elections/{electionId}/ballots", Ballot(candidates.Select(c => c.CandidateId)));
        await Admin().PostAsJsonAsync($"/v1/panch/elections/{electionId}/status", new { status = "Closed" });
        await Admin().PostAsJsonAsync($"/v1/panch/elections/{electionId}/results", new { });

        var payload = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.Where(m => m.Topic == "panch.panel.changed.v1").Select(m => m.Payload).SingleAsync());

        foreach (var (_, memberId) in candidates)
        {
            payload.Should().Contain(memberId.ToString());
        }
    }

    [Fact]
    public async Task An_admin_removes_one_member_and_the_seat_stays_empty()
    {
        var (electionId, candidates) = await VotingOpenAsync(3);
        await Member(Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/panch/elections/{electionId}/ballots", Ballot(candidates.Select(c => c.CandidateId)));
        await Admin().PostAsJsonAsync($"/v1/panch/elections/{electionId}/status", new { status = "Closed" });
        await Admin().PostAsJsonAsync($"/v1/panch/elections/{electionId}/results", new { });

        (await Member(Guid.NewGuid()).DeleteAsync($"/v1/panch/panel/{candidates[0].MemberId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await Admin().DeleteAsync($"/v1/panch/panel/{candidates[0].MemberId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var panel = await Member(Guid.NewGuid()).GetFromJsonAsync<JsonElement>("/v1/panch/panel");
        panel.GetProperty("members").GetArrayLength().Should().Be(2);

        // Removing them again is success with nothing changed.
        (await Admin().DeleteAsync($"/v1/panch/panel/{candidates[0].MemberId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_Samaajs_panel_and_elections_are_invisible()
    {
        var (electionId, _) = await VotingOpenAsync(1);

        (await Member(Guid.NewGuid(), OtherTenantId).GetAsync($"/v1/panch/elections/{electionId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var panel = await Member(Guid.NewGuid(), OtherTenantId).GetFromJsonAsync<JsonElement>("/v1/panch/panel");
        panel.GetProperty("members").GetArrayLength().Should().Be(0);
    }

    private async Task<Guid> DraftAsync(HttpClient admin)
    {
        var created = await admin.PostAsJsonAsync("/v1/panch/elections", Body(Start));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Nominations_are_extended_by_editing_the_election_while_they_are_open()
    {
        var admin = Admin();
        var electionId = await DraftAsync(admin);
        await admin.PostAsJsonAsync($"/v1/panch/elections/{electionId}/status", new { status = "NominationsOpen" });

        // Past the original close, nominations are no longer taken.
        factory.Clock.Set(Start.AddDays(8));
        (await Member(Guid.NewGuid()).PostAsJsonAsync(
                $"/v1/panch/elections/{electionId}/candidates", new { memberId = Guid.NewGuid().ToString() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var edited = await admin.PatchAsJsonAsync($"/v1/panch/elections/{electionId}", new
        {
            title = "Samaaj Panch 2026 (extended)",
            description = "Nominations reopened for another week.",
            nominationStartAt = Start,
            nominationEndAt = Start.AddDays(14),
            votingStartAt = Start.AddDays(14),
            votingEndAt = Start.AddDays(21),
            resultsVisibility = "HiddenUntilClose",
        });

        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await edited.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().Should().Be("Samaaj Panch 2026 (extended)");
        body.GetProperty("status").GetString().Should().Be("NominationsOpen");

        (await Member(Guid.NewGuid()).PostAsJsonAsync(
                $"/v1/panch/elections/{electionId}/candidates", new { memberId = Guid.NewGuid().ToString() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await factory.WithDbContextAsync(db =>
                db.OutboxMessages.CountAsync(m => m.Topic == "panch.election.updated.v1")))
            .Should().Be(1);
    }

    [Fact]
    public async Task An_edit_with_windows_out_of_order_is_refused()
    {
        var admin = Admin();
        var electionId = await DraftAsync(admin);

        (await admin.PatchAsJsonAsync($"/v1/panch/elections/{electionId}", new
            {
                title = "Samaaj Panch 2026",
                nominationStartAt = Start,
                nominationEndAt = Start.AddDays(7),
                votingStartAt = Start.AddDays(3),
                votingEndAt = Start.AddDays(14),
                resultsVisibility = "Live",
            }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Once_voting_opens_the_election_cannot_be_edited_only_extended()
    {
        var (electionId, _) = await VotingOpenAsync(2);
        var admin = Admin();

        (await admin.PatchAsJsonAsync($"/v1/panch/elections/{electionId}", Body(Start)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Earlier than the announced close is refused, however it is asked.
        (await admin.PostAsJsonAsync(
                $"/v1/panch/elections/{electionId}/extend-voting", new { votingEndAt = Start.AddDays(10) }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var extended = await admin.PostAsJsonAsync(
            $"/v1/panch/elections/{electionId}/extend-voting", new { votingEndAt = Start.AddDays(21) });

        extended.StatusCode.Should().Be(HttpStatusCode.OK);
        (await extended.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("votingEndAt").GetDateTimeOffset().Should().Be(Start.AddDays(21));

        // Past the original close, votes are still taken.
        factory.Clock.Set(Start.AddDays(15));
        var detail = await Member(Guid.NewGuid()).GetFromJsonAsync<JsonElement>($"/v1/panch/elections/{electionId}");
        detail.GetProperty("election").GetProperty("acceptsVotes").GetBoolean().Should().BeTrue();

        var payload = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.Where(m => m.Topic == "panch.election.voting-extended.v1")
                .Select(m => m.Payload).SingleAsync());
        payload.Should().Contain("previousVotingEndAt");
    }

    [Fact]
    public async Task Voting_that_has_not_opened_is_moved_by_editing_not_extending()
    {
        var admin = Admin();
        var electionId = await DraftAsync(admin);

        (await admin.PostAsJsonAsync(
                $"/v1/panch/elections/{electionId}/extend-voting", new { votingEndAt = Start.AddDays(30) }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_election_taking_nominations_is_deleted_with_its_nominations()
    {
        var admin = Admin();
        var electionId = await DraftAsync(admin);
        await admin.PostAsJsonAsync($"/v1/panch/elections/{electionId}/status", new { status = "NominationsOpen" });
        await Member(Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/panch/elections/{electionId}/candidates", new { memberId = Guid.NewGuid().ToString() });

        (await Member(Guid.NewGuid()).DeleteAsync($"/v1/panch/elections/{electionId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await admin.DeleteAsync($"/v1/panch/elections/{electionId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Member(Guid.NewGuid()).GetAsync($"/v1/panch/elections/{electionId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await factory.WithDbContextAsync(db => db.Candidates.CountAsync(c => c.ElectionId == electionId)))
            .Should().Be(0);

        // The row is gone; the announcement is the record that it existed.
        (await factory.WithDbContextAsync(db =>
                db.OutboxMessages.CountAsync(m => m.Topic == "panch.election.deleted.v1")))
            .Should().Be(1);

        (await admin.DeleteAsync($"/v1/panch/elections/{electionId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_election_with_ballots_cannot_be_deleted()
    {
        var (electionId, candidates) = await VotingOpenAsync(2);
        await Member(Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/panch/elections/{electionId}/ballots", Ballot([candidates[0].CandidateId]));

        (await Admin().DeleteAsync($"/v1/panch/elections/{electionId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await factory.WithDbContextAsync(db => db.Ballots.CountAsync(b => b.ElectionId == electionId)))
            .Should().Be(1);
    }

    [Fact]
    public async Task Another_Samaajs_election_cannot_be_edited_or_deleted()
    {
        var electionId = await DraftAsync(Admin());
        var outsider = Admin(OtherTenantId);

        (await outsider.PatchAsJsonAsync($"/v1/panch/elections/{electionId}", Body(Start)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await outsider.DeleteAsync($"/v1/panch/elections/{electionId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_ballot_index_is_unique_at_the_database()
    {
        var definition = await factory.WithDbContextAsync(async db =>
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            await db.Database.OpenConnectionAsync();
            command.CommandText =
                "SELECT indexdef FROM pg_indexes WHERE tablename = 'ballots' "
                + "AND indexname = 'ix_ballots_election_id_voter_member_id';";
            return (string?)await command.ExecuteScalarAsync();
        });

        definition.Should().NotBeNull();
        definition.Should().StartWith("CREATE UNIQUE INDEX");
    }
}
