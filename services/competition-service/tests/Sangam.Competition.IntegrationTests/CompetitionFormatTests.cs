using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.Competition.Application.Security;
using Xunit;

namespace Sangam.Competition.IntegrationTests;

/// <summary>
/// A competition run from its format (2026-10-25): rounds and entries made
/// when judging starts, an audition whose top N go through, judges marking
/// only their own panel's rounds out of the competition's max, the
/// organiser's scoresheet, and the published winners.
/// </summary>
public sealed class CompetitionFormatTests(CompetitionApiFactory factory)
    : IClassFixture<CompetitionApiFactory>, IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly Guid _asha = Guid.NewGuid();
    private readonly Guid _bina = Guid.NewGuid();
    private readonly Guid _chetan = Guid.NewGuid();
    private readonly Guid _mainJudge = Guid.NewGuid();
    private readonly Guid _auditionJudge = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Organiser() =>
        factory.CreateClientAs(
            Guid.NewGuid(), Tenant, [], [PermissionKeys.MembersRead, PermissionKeys.CompetitionManage]);

    private HttpClient Member(Guid userId) =>
        factory.CreateClientAs(userId, Tenant, [], [PermissionKeys.MembersRead]);

    private static object Body(
        int? roundCount = null,
        int? maxMarks = null,
        bool? auditionRequired = null,
        string? auditionDate = null,
        int? finalistCount = null) => new
    {
        title = "Elocution",
        description = (string?)null,
        competitionTypeId = (string?)null,
        registrationStartAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        registrationEndAt = DateTimeOffset.UtcNow.AddDays(7),
        roundCount,
        maxMarks,
        auditionRequired,
        auditionDate,
        finalistCount,
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Creates it, registers these members, and starts judging.</summary>
    private async Task<Guid> StartJudgingAsync(HttpClient organiser, object body, params Guid[] members)
    {
        var created = await organiser.PostAsJsonAsync("/v1/competitions", body);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await Json(created)).GetProperty("id").GetGuid();

        await Move(organiser, id, "RegistrationOpen");

        foreach (var member in members)
        {
            (await Member(member).PostAsync($"/v1/competitions/{id}/registrations", null))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await Move(organiser, id, "RegistrationClosed");
        await Move(organiser, id, "InProgress");

        return id;
    }

    private static async Task Move(HttpClient organiser, Guid id, string status) =>
        (await organiser.PostAsJsonAsync($"/v1/competitions/{id}/status", new { status }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    private static async Task Judge(HttpClient organiser, Guid id, Guid judge, string panel) =>
        (await organiser.PostAsJsonAsync($"/v1/competitions/{id}/judges", new { memberId = judge.ToString(), assign = true, panel }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    /// <summary>The judge's own sheet, as round name → (participant → entry id).</summary>
    private async Task<Dictionary<string, Dictionary<Guid, Guid>>> SheetOf(Guid judge, Guid id)
    {
        var sheet = await Json(await Member(judge).GetAsync($"/v1/competitions/{id}/judging/mine"));

        return sheet.GetProperty("rounds").EnumerateArray().ToDictionary(
            r => r.GetProperty("round").GetProperty("name").GetString()!,
            r => r.GetProperty("entries").EnumerateArray().ToDictionary(
                e => e.GetProperty("participantMemberId").GetGuid(),
                e => e.GetProperty("entryId").GetGuid()));
    }

    private async Task<HttpStatusCode> Mark(Guid judge, Guid id, Guid entryId, int value) =>
        (await Member(judge).PostAsJsonAsync($"/v1/competitions/{id}/entries/{entryId}/scores", new { value, comments = (string?)null }))
            .StatusCode;

    private async Task<Dictionary<string, (Guid Id, string Kind)>> RoundsOf(HttpClient organiser, Guid id)
    {
        var detail = await Json(await organiser.GetAsync($"/v1/competitions/{id}"));

        return detail.GetProperty("rounds").EnumerateArray().ToDictionary(
            r => r.GetProperty("name").GetString()!,
            r => (r.GetProperty("id").GetGuid(), r.GetProperty("kind").GetString()!));
    }

    [Fact]
    public async Task The_format_is_asked_for_up_front_and_round_trips()
    {
        var response = await Organiser().PostAsJsonAsync(
            "/v1/competitions", Body(roundCount: 2, maxMarks: 10, auditionRequired: true,
                auditionDate: DateTime.UtcNow.AddDays(8).ToString("yyyy-MM-dd"), finalistCount: 4));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await Json(response);
        created.GetProperty("roundCount").GetInt32().Should().Be(2);
        created.GetProperty("maxMarks").GetInt32().Should().Be(10);
        created.GetProperty("auditionRequired").GetBoolean().Should().BeTrue();
        created.GetProperty("finalistCount").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task A_competition_left_without_a_format_is_one_judging_out_of_100()
    {
        var created = await Json(await Organiser().PostAsJsonAsync("/v1/competitions", Body()));

        created.GetProperty("roundCount").GetInt32().Should().Be(0);
        created.GetProperty("maxMarks").GetInt32().Should().Be(100);
        created.GetProperty("auditionRequired").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(true, null, 3, "AuditionDate")]
    [InlineData(true, "2099-01-01", null, "FinalistCount")]
    public async Task An_audition_needs_its_date_and_how_many_go_through(
        bool auditionRequired, string? auditionDate, int? finalistCount, string field)
    {
        var response = await Organiser().PostAsJsonAsync(
            "/v1/competitions", Body(auditionRequired: auditionRequired, auditionDate: auditionDate, finalistCount: finalistCount));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(field);
    }

    [Fact]
    public async Task Max_marks_of_zero_is_refused()
    {
        (await Organiser().PostAsJsonAsync("/v1/competitions", Body(maxMarks: 0)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Starting_judging_enters_every_applicant_in_every_round()
    {
        var organiser = Organiser();
        var id = await StartJudgingAsync(organiser, Body(roundCount: 2, maxMarks: 10), _asha, _bina);

        var rounds = await RoundsOf(organiser, id);
        rounds.Keys.Should().BeEquivalentTo(["Round 1", "Round 2"]);

        await Judge(organiser, id, _mainJudge, "Main");
        var sheet = await SheetOf(_mainJudge, id);

        sheet["Round 1"].Keys.Should().BeEquivalentTo([_asha, _bina]);
        sheet["Round 2"].Keys.Should().BeEquivalentTo([_asha, _bina]);
    }

    [Fact]
    public async Task A_mark_is_out_of_the_competitions_own_max()
    {
        var organiser = Organiser();
        var id = await StartJudgingAsync(organiser, Body(maxMarks: 10), _asha);
        await Judge(organiser, id, _mainJudge, "Main");

        var entry = (await SheetOf(_mainJudge, id))["Final"][_asha];

        (await Mark(_mainJudge, id, entry, 11)).Should().Be(HttpStatusCode.BadRequest);
        (await Mark(_mainJudge, id, entry, 10)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_an_audition_a_judge_may_only_sit_on_the_main_panel()
    {
        var organiser = Organiser();
        var id = await StartJudgingAsync(organiser, Body(), _asha);

        (await organiser.PostAsJsonAsync($"/v1/competitions/{id}/judges", new { memberId = _auditionJudge.ToString(), assign = true, panel = "Audition" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_judge_cannot_mark_their_own_entry_and_does_not_see_it()
    {
        var organiser = Organiser();
        var id = await StartJudgingAsync(organiser, Body(), _asha, _mainJudge);
        await Judge(organiser, id, _mainJudge, "Main");

        var sheet = await SheetOf(_mainJudge, id);
        sheet["Final"].Keys.Should().BeEquivalentTo([_asha]);

        var scoresheet = await Json(await organiser.GetAsync($"/v1/competitions/{id}/scoresheet"));
        var own = scoresheet.GetProperty("rounds")[0].GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("participantMemberId").GetGuid() == _mainJudge)
            .GetProperty("entryId").GetGuid();

        (await Mark(_mainJudge, id, own, 50)).Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_audition_decides_who_takes_part_and_only_its_panel_marks_it()
    {
        var organiser = Organiser();
        var id = await StartJudgingAsync(
            organiser,
            Body(roundCount: 2, maxMarks: 10, auditionRequired: true,
                auditionDate: DateTime.UtcNow.AddDays(8).ToString("yyyy-MM-dd"), finalistCount: 1),
            _asha, _bina, _chetan);

        await Judge(organiser, id, _auditionJudge, "Audition");
        await Judge(organiser, id, _mainJudge, "Main");

        var rounds = await RoundsOf(organiser, id);
        rounds["Audition"].Kind.Should().Be("Audition");

        // Everybody auditions; nobody is in a main round yet.
        var audition = (await SheetOf(_auditionJudge, id))["Audition"];
        audition.Keys.Should().BeEquivalentTo([_asha, _bina, _chetan]);
        (await SheetOf(_mainJudge, id))["Round 1"].Should().BeEmpty();

        // A main-panel judge may not mark the audition.
        (await Mark(_mainJudge, id, audition[_asha], 5)).Should().Be(HttpStatusCode.Forbidden);

        (await Mark(_auditionJudge, id, audition[_asha], 9)).Should().Be(HttpStatusCode.OK);
        (await Mark(_auditionJudge, id, audition[_bina], 6)).Should().Be(HttpStatusCode.OK);

        (await organiser.PostAsync($"/v1/competitions/{id}/rounds/{rounds["Audition"].Id}/close", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The top one went through to every main round; Chetan was never marked.
        var main = await SheetOf(_mainJudge, id);
        main["Round 1"].Keys.Should().BeEquivalentTo([_asha]);
        main["Round 2"].Keys.Should().BeEquivalentTo([_asha]);

        var scoresheet = await Json(await organiser.GetAsync($"/v1/competitions/{id}/scoresheet"));
        var decided = scoresheet.GetProperty("rounds").EnumerateArray()
            .Single(r => r.GetProperty("round").GetProperty("kind").GetString() == "Audition")
            .GetProperty("entries").EnumerateArray()
            .ToDictionary(e => e.GetProperty("participantMemberId").GetGuid(), e => e.GetProperty("wentThrough").GetBoolean());

        decided[_asha].Should().BeTrue();
        decided[_bina].Should().BeFalse();
        decided[_chetan].Should().BeFalse();

        // An audition judge does not mark the main rounds.
        (await Mark(_auditionJudge, id, main["Round 1"][_asha], 5)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_audition_nobody_has_marked_is_not_closed_with_no_finalists()
    {
        var organiser = Organiser();
        var id = await StartJudgingAsync(
            organiser,
            Body(auditionRequired: true, auditionDate: DateTime.UtcNow.AddDays(8).ToString("yyyy-MM-dd"), finalistCount: 2),
            _asha);

        var audition = (await RoundsOf(organiser, id))["Audition"].Id;

        (await organiser.PostAsync($"/v1/competitions/{id}/rounds/{audition}/close", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_winners_are_the_highest_totals_and_every_judges_marks_are_the_organisers_to_see()
    {
        var organiser = Organiser();
        var id = await StartJudgingAsync(organiser, Body(roundCount: 2, maxMarks: 10), _asha, _bina, _chetan);
        var secondJudge = Guid.NewGuid();

        await Judge(organiser, id, _mainJudge, "Main");
        await Judge(organiser, id, secondJudge, "Main");

        var sheet = await SheetOf(_mainJudge, id);

        // Round 1: Asha 8 & 6 (avg 7), Bina 9, Chetan 5. Round 2: Asha 9, Bina 6, Chetan 5.
        // Totals: Asha 16, Bina 15, Chetan 10.
        await Mark(_mainJudge, id, sheet["Round 1"][_asha], 8);
        await Mark(secondJudge, id, sheet["Round 1"][_asha], 6);
        await Mark(_mainJudge, id, sheet["Round 1"][_bina], 9);
        await Mark(_mainJudge, id, sheet["Round 1"][_chetan], 5);
        await Mark(_mainJudge, id, sheet["Round 2"][_asha], 9);
        await Mark(_mainJudge, id, sheet["Round 2"][_bina], 6);
        await Mark(_mainJudge, id, sheet["Round 2"][_chetan], 5);

        var scoresheet = await Json(await organiser.GetAsync($"/v1/competitions/{id}/scoresheet"));
        var ashaRound1 = scoresheet.GetProperty("rounds")[0].GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("participantMemberId").GetGuid() == _asha);

        ashaRound1.GetProperty("marks").GetArrayLength().Should().Be(2);
        ashaRound1.GetProperty("average").GetDouble().Should().Be(7);

        // Not until every round is closed.
        (await organiser.PostAsync($"/v1/competitions/{id}/result", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var rounds = await RoundsOf(organiser, id);
        await organiser.PostAsync($"/v1/competitions/{id}/rounds/{rounds["Round 1"].Id}/close", null);
        await organiser.PostAsync($"/v1/competitions/{id}/rounds/{rounds["Round 2"].Id}/close", null);

        (await Member(_bina).GetAsync($"/v1/competitions/{id}/result")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var published = await organiser.PostAsync($"/v1/competitions/{id}/result", null);
        published.StatusCode.Should().Be(HttpStatusCode.OK);

        var seen = await Json(await Member(_bina).GetAsync($"/v1/competitions/{id}/result"));
        var winners = seen.GetProperty("winners").EnumerateArray().ToList();

        winners.Select(w => w.GetProperty("memberId").GetGuid()).Should().Equal(_asha, _bina, _chetan);
        winners[0].GetProperty("total").GetDouble().Should().Be(16);
        winners[1].GetProperty("rank").GetInt32().Should().Be(2);

        // A member sees the podium, not every judge's marks or every place.
        seen.GetProperty("standings").GetArrayLength().Should().Be(0);
        (await Member(_bina).GetAsync($"/v1/competitions/{id}/scoresheet")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var detail = await Json(await Member(_bina).GetAsync($"/v1/competitions/{id}"));
        detail.GetProperty("hasPublishedResult").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Only_a_judge_has_a_judging_sheet()
    {
        var organiser = Organiser();
        var id = await StartJudgingAsync(organiser, Body(), _asha);

        (await Member(_bina).GetAsync($"/v1/competitions/{id}/judging/mine"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
