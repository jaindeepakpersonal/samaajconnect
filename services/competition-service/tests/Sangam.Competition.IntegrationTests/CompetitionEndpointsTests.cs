using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.Competition.Application.Security;
using Xunit;

namespace Sangam.Competition.IntegrationTests;

/// <summary>
/// Competitions, rounds, entries and scores through their endpoints, against
/// a real database. The tenant query filter is applied by the DbContext
/// rather than by any handler, and the outbox guarantee is transactional -
/// neither can be shown against a substituted repository.
/// </summary>
public sealed class CompetitionEndpointsTests(CompetitionApiFactory factory)
    : IClassFixture<CompetitionApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Organiser(Guid? tenantId = null) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId ?? TenantA, [], [PermissionKeys.MembersRead, PermissionKeys.CompetitionManage]);

    private HttpClient Member(Guid userId, Guid? tenantId = null) =>
        factory.CreateClientAs(userId, tenantId ?? TenantA, [], [PermissionKeys.MembersRead]);

    private static object CreateCompetitionBody(
        string title = "Rangoli Contest",
        DateTimeOffset? registrationStartAt = null,
        DateTimeOffset? registrationEndAt = null) => new
    {
        title,
        description = "Annual rangoli making contest",
        competitionTypeId = (string?)null,
        registrationStartAt = registrationStartAt ?? DateTimeOffset.UtcNow.AddMinutes(-5),
        registrationEndAt = registrationEndAt ?? DateTimeOffset.UtcNow.AddDays(7),
    };

    private async Task<Guid> CreateOpenCompetitionAsync(HttpClient organiser)
    {
        var created = await organiser.PostAsJsonAsync("/v1/competitions", CreateCompetitionBody());
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        (await organiser.PostAsJsonAsync($"/v1/competitions/{id}/status", new { status = "RegistrationOpen" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return id;
    }

    // ---- Competitions and registration --------------------------------------

    [Fact]
    public async Task Creating_a_competition_starts_as_draft_and_round_trips_its_fields()
    {
        var organiser = Organiser();

        var response = await organiser.PostAsJsonAsync("/v1/competitions", CreateCompetitionBody());
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("title").GetString().Should().Be("Rangoli Contest");
        created.GetProperty("status").GetString().Should().Be("Draft");
    }

    [Fact]
    public async Task A_competition_is_corrected_until_registration_closes_and_not_after()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);
        var locality = Guid.NewGuid();

        var body = new
        {
            title = "Rangoli Contest 2026",
            description = "Now with a theme",
            competitionTypeId = (string?)null,
            registrationStartAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            registrationEndAt = DateTimeOffset.UtcNow.AddDays(10),
            localityId = locality.ToString(),
        };

        var updated = await organiser.PatchAsJsonAsync($"/v1/competitions/{id}", body);

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var competition = await updated.Content.ReadFromJsonAsync<JsonElement>();
        competition.GetProperty("title").GetString().Should().Be("Rangoli Contest 2026");
        competition.GetProperty("localityId").GetGuid().Should().Be(locality);

        (await organiser.GetFromJsonAsync<JsonElement>($"/v1/competitions?localityId={locality}"))
            .EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().Equal(id);
        (await organiser.GetFromJsonAsync<JsonElement>($"/v1/competitions?localityId={Guid.NewGuid()}"))
            .GetArrayLength().Should().Be(0);

        (await organiser.PostAsJsonAsync($"/v1/competitions/{id}/status", new { status = "RegistrationClosed" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await organiser.PatchAsJsonAsync($"/v1/competitions/{id}", body))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "members have signed up to these details");
    }

    [Fact]
    public async Task A_member_cannot_edit_a_competition()
    {
        var id = await CreateOpenCompetitionAsync(Organiser());

        (await Member(Guid.NewGuid()).PatchAsJsonAsync($"/v1/competitions/{id}", CreateCompetitionBody("Mine")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Status_moves_are_strictly_forward()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);

        // Cannot skip a stage.
        var skip = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/status", new { status = "InProgress" });
        skip.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await organiser.PostAsJsonAsync($"/v1/competitions/{id}/status", new { status = "RegistrationClosed" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // No route back.
        var back = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/status", new { status = "RegistrationOpen" });
        back.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Registering_twice_is_reported_as_success_with_registered_false()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);
        var member = Member(Guid.NewGuid());

        var first = await member.PostAsync($"/v1/competitions/{id}/registrations", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("registered").GetBoolean().Should().BeTrue();

        var second = await member.PostAsync($"/v1/competitions/{id}/registrations", null);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("registered").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Registration_outside_the_open_window_is_refused()
    {
        var organiser = Organiser();

        var created = await organiser.PostAsJsonAsync("/v1/competitions", CreateCompetitionBody());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Still Draft - never opened.
        var response = await Member(Guid.NewGuid()).PostAsync($"/v1/competitions/{id}/registrations", null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Withdrawing_then_registering_again_reactivates_the_same_registration()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);
        var member = Member(Guid.NewGuid());

        await member.PostAsync($"/v1/competitions/{id}/registrations", null);

        (await member.DeleteAsync($"/v1/competitions/{id}/registrations/mine"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var reregistered = await member.PostAsync($"/v1/competitions/{id}/registrations", null);
        reregistered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reregistered.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("registered").GetBoolean().Should().BeTrue();
    }

    // ---- Judges, rounds and entries -----------------------------------------

    [Fact]
    public async Task Assigning_and_removing_a_judge_round_trips()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);
        var judgeId = Guid.NewGuid();

        var assign = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/judges", new { memberId = judgeId.ToString(), assign = true });
        assign.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assigning the same judge again is refused.
        (await organiser.PostAsJsonAsync(
                $"/v1/competitions/{id}/judges", new { memberId = judgeId.ToString(), assign = true }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var remove = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/judges", new { memberId = judgeId.ToString(), assign = false });
        remove.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_malformed_member_id_is_a_validation_problem_not_a_crash()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);

        var response = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/judges", new { memberId = "not-a-guid", assign = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Creating_an_entry_requires_the_participant_to_be_registered()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);

        var round = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/rounds", new { name = "Round 1" });
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var unregistered = Guid.NewGuid();

        var refused = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/rounds/{roundId}/entries",
            new { participantMemberId = unregistered.ToString() });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_entry_is_refused_a_second_time_for_the_same_participant_in_the_same_round()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);
        var participant = Guid.NewGuid();

        await Member(participant).PostAsync($"/v1/competitions/{id}/registrations", null);

        var round = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/rounds", new { name = "Round 1" });
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var first = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/rounds/{roundId}/entries",
            new { participantMemberId = participant.ToString() });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/rounds/{roundId}/entries",
            new { participantMemberId = participant.ToString() });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Entries_are_refused_once_the_round_is_closed()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);
        var participant = Guid.NewGuid();

        await Member(participant).PostAsync($"/v1/competitions/{id}/registrations", null);

        var round = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/rounds", new { name = "Round 1" });
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await organiser.PostAsync($"/v1/competitions/{id}/rounds/{roundId}/close", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/rounds/{roundId}/entries",
            new { participantMemberId = participant.ToString() });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- Scoring, closing and publishing -------------------------------------

    [Fact]
    public async Task Only_an_assigned_judge_may_record_a_score()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);
        var participant = Guid.NewGuid();
        var notAJudge = Guid.NewGuid();

        await Member(participant).PostAsync($"/v1/competitions/{id}/registrations", null);

        var round = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/rounds", new { name = "Round 1" });
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var entry = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/rounds/{roundId}/entries",
            new { participantMemberId = participant.ToString() });
        var entryId = (await entry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var refused = await Member(notAJudge).PostAsJsonAsync(
            $"/v1/competitions/{id}/entries/{entryId}/scores", new { value = 80, comments = (string?)null });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Recording_a_score_twice_updates_it_rather_than_being_refused()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);
        var participant = Guid.NewGuid();
        var judgeId = Guid.NewGuid();

        await Member(participant).PostAsync($"/v1/competitions/{id}/registrations", null);
        await organiser.PostAsJsonAsync($"/v1/competitions/{id}/judges", new { memberId = judgeId.ToString(), assign = true });

        var round = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/rounds", new { name = "Round 1" });
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var entry = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/rounds/{roundId}/entries",
            new { participantMemberId = participant.ToString() });
        var entryId = (await entry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var judge = Member(judgeId);

        (await judge.PostAsJsonAsync($"/v1/competitions/{id}/entries/{entryId}/scores", new { value = 60, comments = "First pass" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var corrected = await judge.PostAsJsonAsync(
            $"/v1/competitions/{id}/entries/{entryId}/scores", new { value = 85, comments = "Corrected" });
        corrected.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await corrected.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("value").GetInt32().Should().Be(85);
        body.GetProperty("comments").GetString().Should().Be("Corrected");

        // Still one row, not two - the round's own view only ever shows one mark from this judge.
        var roundView = await judge.GetFromJsonAsync<JsonElement>($"/v1/competitions/{id}/rounds/{roundId}");
        var entryView = roundView.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == entryId);
        entryView.GetProperty("myScore").GetInt32().Should().Be(85);
    }

    [Fact]
    public async Task Scores_are_hidden_from_an_ordinary_member_but_visible_to_the_organiser_and_judges()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);
        var participant = Guid.NewGuid();
        var judgeId = Guid.NewGuid();
        var onlooker = Guid.NewGuid();

        await Member(participant).PostAsync($"/v1/competitions/{id}/registrations", null);
        await organiser.PostAsJsonAsync($"/v1/competitions/{id}/judges", new { memberId = judgeId.ToString(), assign = true });

        var round = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/rounds", new { name = "Round 1" });
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var entry = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/rounds/{roundId}/entries",
            new { participantMemberId = participant.ToString() });
        var entryId = (await entry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await Member(judgeId).PostAsJsonAsync($"/v1/competitions/{id}/entries/{entryId}/scores", new { value = 70, comments = (string?)null });

        var asOnlooker = await Member(onlooker).GetFromJsonAsync<JsonElement>($"/v1/competitions/{id}/rounds/{roundId}");
        asOnlooker.GetProperty("canSeeScores").GetBoolean().Should().BeFalse();
        asOnlooker.GetProperty("entries").EnumerateArray().Single().GetProperty("averageScore").ValueKind
            .Should().Be(JsonValueKind.Null);

        var asOrganiser = await organiser.GetFromJsonAsync<JsonElement>($"/v1/competitions/{id}/rounds/{roundId}");
        asOrganiser.GetProperty("canSeeScores").GetBoolean().Should().BeTrue();
        asOrganiser.GetProperty("entries").EnumerateArray().Single().GetProperty("averageScore").GetDouble().Should().Be(70);
    }

    [Fact]
    public async Task Publishing_requires_a_closed_round_and_freezes_the_ranking()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);
        var strongParticipant = Guid.NewGuid();
        var weakParticipant = Guid.NewGuid();
        var judgeId = Guid.NewGuid();

        await Member(strongParticipant).PostAsync($"/v1/competitions/{id}/registrations", null);
        await Member(weakParticipant).PostAsync($"/v1/competitions/{id}/registrations", null);
        await organiser.PostAsJsonAsync($"/v1/competitions/{id}/judges", new { memberId = judgeId.ToString(), assign = true });

        var round = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/rounds", new { name = "Round 1" });
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var strongEntry = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/rounds/{roundId}/entries",
            new { participantMemberId = strongParticipant.ToString() });
        var strongEntryId = (await strongEntry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var weakEntry = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{id}/rounds/{roundId}/entries",
            new { participantMemberId = weakParticipant.ToString() });
        var weakEntryId = (await weakEntry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var judge = Member(judgeId);
        await judge.PostAsJsonAsync($"/v1/competitions/{id}/entries/{strongEntryId}/scores", new { value = 95, comments = (string?)null });
        await judge.PostAsJsonAsync($"/v1/competitions/{id}/entries/{weakEntryId}/scores", new { value = 40, comments = (string?)null });

        // Publishing before the round closes is refused.
        var early = await organiser.PostAsync($"/v1/competitions/{id}/rounds/{roundId}/results", null);
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await organiser.PostAsync($"/v1/competitions/{id}/rounds/{roundId}/close", null);

        var published = await organiser.PostAsync($"/v1/competitions/{id}/rounds/{roundId}/results", null);
        published.StatusCode.Should().Be(HttpStatusCode.OK);

        var publishedBody = await published.Content.ReadFromJsonAsync<JsonElement>();
        var ranking = publishedBody.GetProperty("ranking");
        ranking[0].GetProperty("entryId").GetGuid().Should().Be(strongEntryId);
        ranking[0].GetProperty("rank").GetInt32().Should().Be(1);
        ranking[1].GetProperty("entryId").GetGuid().Should().Be(weakEntryId);

        // Everyone, including an ordinary member, can see the published ranking.
        var asOnlooker = await Member(Guid.NewGuid())
            .GetFromJsonAsync<JsonElement>($"/v1/competitions/{id}/rounds/{roundId}/results");
        asOnlooker.GetProperty("ranking")[0].GetProperty("entryId").GetGuid().Should().Be(strongEntryId);

        // Publishing again returns the same frozen result rather than an error.
        var again = await organiser.PostAsync($"/v1/competitions/{id}/rounds/{roundId}/results", null);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("publishedAt").GetDateTimeOffset()
            .Should().BeCloseTo(publishedBody.GetProperty("publishedAt").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1),
                "Postgres stores microsecond precision, so a round trip through it can lose sub-microsecond ticks");
    }

    [Fact]
    public async Task Publishing_a_round_with_no_entries_is_refused()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);

        var round = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/rounds", new { name = "Round 1" });
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await organiser.PostAsync($"/v1/competitions/{id}/rounds/{roundId}/close", null);

        var response = await organiser.PostAsync($"/v1/competitions/{id}/rounds/{roundId}/results", null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_result_is_not_reachable_before_it_is_published()
    {
        var organiser = Organiser();
        var id = await CreateOpenCompetitionAsync(organiser);

        var round = await organiser.PostAsJsonAsync($"/v1/competitions/{id}/rounds", new { name = "Round 1" });
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await organiser.GetAsync($"/v1/competitions/{id}/rounds/{roundId}/results");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Tenant isolation -----------------------------------------------------

    [Fact]
    public async Task A_competition_is_invisible_from_a_different_Samaaj()
    {
        var organiserA = Organiser(TenantA);
        var id = await CreateOpenCompetitionAsync(organiserA);

        var organiserB = Organiser(TenantB);
        var response = await organiserB.GetAsync($"/v1/competitions/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_this_Samaajs_own_competitions_appear_in_the_list()
    {
        await CreateOpenCompetitionAsync(Organiser(TenantA));
        await CreateOpenCompetitionAsync(Organiser(TenantB));

        var listA = await Organiser(TenantA).GetFromJsonAsync<JsonElement>("/v1/competitions");

        listA.EnumerateArray().Should().HaveCount(1);
    }
}
