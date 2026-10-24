using Sangam.Competition.Domain.Competitions;
using Sangam.Competition.Domain.Entries;
using CompetitionAggregate = Sangam.Competition.Domain.Competitions.Competition;

namespace Sangam.Competition.Application.Competitions;

/// <summary>The one place a competition/round/entry becomes a response.</summary>
internal static class CompetitionMappings
{
    public static CompetitionResponse ToResponse(this CompetitionAggregate competition, Guid? memberId) =>
        new(
            competition.Id,
            competition.Title,
            competition.Description,
            competition.CompetitionTypeId,
            competition.RegistrationStartAt,
            competition.RegistrationEndAt,
            competition.Status.ToString(),
            competition.AcceptsRegistrations(DateTimeOffset.UtcNow),
            competition.Registrations.Count(r => r.Status == RegistrationStatus.Registered),
            memberId is { } id ? competition.FindRegistration(id)?.Status.ToString() : null,
            memberId is { } judgeId && competition.IsJudge(judgeId),
            competition.CreatedAt,
            competition.LocalityId,
            competition.Venue,
            competition.CityId,
            competition.PhotoId is { } photoId ? $"/v1/competitions/{competition.Id}/photo?v={photoId:N}" : null);

    public static RoundResponse ToResponse(this Round round, int entryCount, bool hasPublishedResult) =>
        new(
            round.Id,
            round.Name,
            round.SequenceNumber,
            round.Status.ToString(),
            round.CreatedAt,
            round.ClosedAt,
            entryCount,
            hasPublishedResult);

    public static JudgeResponse ToResponse(this CompetitionJudge judge) =>
        new(judge.Id, judge.MemberId, judge.AssignedAt);

    /// <summary>
    /// Ranks a round's entries by average mark, best first.
    /// </summary>
    /// <remarks>
    /// Ties break on earliest entry creation, which is arbitrary but stable
    /// - the same tie-break shape `VotingCampaign.RankBy` already uses on
    /// nomination order. An entry nobody scored ranks with a zero average,
    /// same as an unscored candidate would poll zero votes.
    /// </remarks>
    public static IReadOnlyList<Entry> RankBy(
        this IReadOnlyList<Entry> entries, IReadOnlyDictionary<Guid, double> averages) =>
        [.. entries
            .OrderByDescending(e => averages.GetValueOrDefault(e.Id))
            .ThenBy(e => e.CreatedAt)];
}
