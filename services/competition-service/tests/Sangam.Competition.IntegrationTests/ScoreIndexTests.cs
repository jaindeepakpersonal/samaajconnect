using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Competition.Application.Security;
using Xunit;

namespace Sangam.Competition.IntegrationTests;

/// <summary>
/// Asserts the mechanism `IScoreRepository.UpsertAsync` relies on: the
/// unique index on <c>(entry_id, judge_member_id)</c>.
/// </summary>
/// <remarks>
/// Unlike celebrity-voting-service's own <c>VoteIndexTests</c>, a second
/// write here is not something to refuse - it is the ordinary case (a judge
/// correcting a mark). What has to hold is that concurrent writes from the
/// same judge for the same entry never leave two rows behind, which is what
/// would make "the entry's average score" ambiguous. These tests name the
/// index directly, and then prove concurrent HTTP requests still converge on
/// one row through it.
/// </remarks>
public sealed class ScoreIndexTests(CompetitionApiFactory factory)
    : IClassFixture<CompetitionApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Organiser() =>
        factory.CreateClientAs(
            Guid.NewGuid(), TenantId, [], [PermissionKeys.MembersRead, PermissionKeys.CompetitionManage]);

    private HttpClient Member(Guid userId) =>
        factory.CreateClientAs(userId, TenantId, [], [PermissionKeys.MembersRead]);

    [Fact]
    public async Task The_scores_index_exists_and_is_unique()
    {
        // Read from the live schema rather than from the EF model: the model
        // is what we asked for, and this is what the database actually built.
        var definition = await factory.WithDbContextAsync(async db =>
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();

            await db.Database.OpenConnectionAsync();

            command.CommandText =
                "SELECT indexdef FROM pg_indexes "
                + "WHERE tablename = 'scores' AND indexname = 'ix_scores_entry_id_judge_member_id';";

            return (string?)await command.ExecuteScalarAsync();
        });

        definition.Should().NotBeNull(
            "the index on (entry_id, judge_member_id) is what makes an upsert well-defined");

        definition.Should().StartWith("CREATE UNIQUE INDEX",
            "a non-unique index would let two rows exist for one judge's mark on one entry, "
            + "making the entry's average ambiguous");
    }

    [Fact]
    public async Task Concurrent_scores_from_one_judge_for_one_entry_converge_on_a_single_row()
    {
        var organiser = Organiser();
        var participant = Guid.NewGuid();
        var judgeId = Guid.NewGuid();

        var created = await organiser.PostAsJsonAsync("/v1/competitions", new
        {
            title = "Rangoli Contest",
            description = (string?)null,
            competitionTypeId = (string?)null,
            registrationStartAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            registrationEndAt = DateTimeOffset.UtcNow.AddDays(7),
        });

        var competitionId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await organiser.PostAsJsonAsync($"/v1/competitions/{competitionId}/status", new { status = "RegistrationOpen" });
        await Member(participant).PostAsync($"/v1/competitions/{competitionId}/registrations", null);
        await organiser.PostAsJsonAsync(
            $"/v1/competitions/{competitionId}/judges", new { memberId = judgeId.ToString(), assign = true });

        var round = await organiser.PostAsJsonAsync($"/v1/competitions/{competitionId}/rounds", new { name = "Round 1" });
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var entry = await organiser.PostAsJsonAsync(
            $"/v1/competitions/{competitionId}/rounds/{roundId}/entries",
            new { participantMemberId = participant.ToString() });
        var entryId = (await entry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var judge = Member(judgeId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            judge.PostAsJsonAsync(
                $"/v1/competitions/{competitionId}/entries/{entryId}/scores",
                new { value = i, comments = (string?)null })));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
            "an upsert never refuses a judge correcting their own mark, concurrently or not");

        var rows = await factory.WithDbContextAsync(db =>
            db.Scores.IgnoreQueryFilters().CountAsync(s => s.EntryId == entryId && s.JudgeMemberId == judgeId));

        rows.Should().Be(1, "the unique index is what keeps twenty racing upserts from leaving two rows behind");
    }
}
