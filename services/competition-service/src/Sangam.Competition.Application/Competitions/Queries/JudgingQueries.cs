using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;
using Sangam.Competition.Domain.Competitions;
using Sangam.Competition.Domain.Entries;
using CompetitionAggregate = Sangam.Competition.Domain.Competitions.Competition;

namespace Sangam.Competition.Application.Competitions.Queries;

/// <summary>
/// The published final standings. 404 until they are published. Everybody
/// sees the winners; only the organiser sees every place.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetCompetitionResultQuery(Guid CompetitionId) : IQuery<CompetitionResultResponse>;

public sealed class GetCompetitionResultQueryHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IScoreRepository scores,
    ICompetitionResultRepository results,
    ICurrentUser currentUser)
    : IRequestHandler<GetCompetitionResultQuery, Result<CompetitionResultResponse>>
{
    public async Task<Result<CompetitionResultResponse>> Handle(
        GetCompetitionResultQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(query.CompetitionId, cancellationToken);

        if (competition is null)
        {
            return Result.Failure<CompetitionResultResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var result = await results.FindByCompetitionAsync(competition.Id, cancellationToken);

        if (result is null)
        {
            return Result.Failure<CompetitionResultResponse>(Error.NotFound(
                "Competition.NotPublished", "This competition has no published result yet."));
        }

        var canSeeAll = currentUser.HasPermission(PermissionKeys.CompetitionManage);

        return Result.Success(await CompetitionResultDescriber.DescribeAsync(
            competition, result, canSeeAll, entries, scores, cancellationToken));
    }
}

/// <summary>
/// Every mark every judge has given, and the standings they add up to - the
/// organiser's panel. Individual marks stay the organiser's and the judges';
/// an ordinary member never sees one.
/// </summary>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record GetScoresheetQuery(Guid CompetitionId) : IQuery<ScoresheetResponse>;

public sealed class GetScoresheetQueryHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IScoreRepository scores,
    IRoundResultRepository roundResults,
    ICompetitionResultRepository results)
    : IRequestHandler<GetScoresheetQuery, Result<ScoresheetResponse>>
{
    public async Task<Result<ScoresheetResponse>> Handle(GetScoresheetQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(query.CompetitionId, cancellationToken);

        if (competition is null)
        {
            return Result.Failure<ScoresheetResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var rounds = competition.Rounds.OrderBy(r => r.SequenceNumber).ToList();
        var allEntries = await entries.ListByRoundsAsync([.. rounds.Select(r => r.Id)], cancellationToken);
        var marks = await scores.ListByEntriesAsync([.. allEntries.Select(e => e.Id)], cancellationToken);

        var marksByEntry = marks.ToLookup(m => m.EntryId);
        var averages = marks
            .GroupBy(m => m.EntryId)
            .ToDictionary(g => g.Key, g => g.Average(m => (double)m.Value));

        var mainRoundIds = competition.MainRounds.Select(r => r.Id).ToHashSet();
        var finalists = allEntries
            .Where(e => mainRoundIds.Contains(e.RoundId))
            .Select(e => e.ParticipantMemberId)
            .ToHashSet();

        var sheetRounds = new List<ScoresheetRoundResponse>(rounds.Count);

        foreach (var round in rounds)
        {
            var inRound = allEntries.Where(e => e.RoundId == round.Id).ToList();
            var hasResult = await roundResults.FindByRoundAsync(round.Id, cancellationToken) is not null;
            var auditionDecided = round.Kind == RoundKind.Audition && round.Status == RoundStatus.Closed;

            sheetRounds.Add(new ScoresheetRoundResponse(
                round.ToResponse(inRound.Count, hasResult),
                [.. inRound
                    .Select(e => new ScoresheetEntryResponse(
                        e.Id,
                        e.ParticipantMemberId,
                        [.. marksByEntry[e.Id]
                            .OrderBy(m => m.CreatedAt)
                            .Select(m => new JudgeMarkResponse(m.JudgeMemberId, m.Value, m.Comments, m.UpdatedAt))],
                        averages.TryGetValue(e.Id, out var average) ? average : null,
                        auditionDecided ? finalists.Contains(e.ParticipantMemberId) : null))
                    .OrderByDescending(e => e.Average ?? -1)]));
        }

        var standings = CompetitionJudging.Standings(competition, allEntries, averages);
        var published = await results.FindByCompetitionAsync(competition.Id, cancellationToken) is not null;

        return Result.Success(new ScoresheetResponse(
            competition.Id,
            competition.MaxMarks,
            [.. competition.Judges.OrderBy(j => j.AssignedAt).Select(j => j.ToResponse())],
            sheetRounds,
            [.. standings.Select(s => s.ToResponse())],
            published));
    }
}

/// <summary>
/// The calling judge's own sheet: each round their panel judges, every
/// participant in it bar themselves, and the marks they have given. 403 for
/// anybody who is not a judge of this competition.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetMyJudgingQuery(Guid CompetitionId) : IQuery<JudgingSheetResponse>;

public sealed class GetMyJudgingQueryHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IScoreRepository scores,
    IRoundResultRepository roundResults,
    ICurrentUser currentUser)
    : IRequestHandler<GetMyJudgingQuery, Result<JudgingSheetResponse>>
{
    public async Task<Result<JudgingSheetResponse>> Handle(GetMyJudgingQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(query.CompetitionId, cancellationToken);

        if (competition is null)
        {
            return Result.Failure<JudgingSheetResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        if (currentUser.UserId is not { } judgeId || competition.FindJudge(judgeId) is not { } judge)
        {
            return Result.Failure<JudgingSheetResponse>(Error.Forbidden(
                "Judge.Required", "You are not an assigned judge for this competition."));
        }

        var rounds = competition.Rounds
            .Where(r => judge.Judges(r.Kind))
            .OrderBy(r => r.SequenceNumber)
            .ToList();

        var theirEntries = (await entries.ListByRoundsAsync([.. rounds.Select(r => r.Id)], cancellationToken))
            .Where(e => e.ParticipantMemberId != judgeId)
            .ToList();

        var mine = (await scores.ListByEntriesAsync([.. theirEntries.Select(e => e.Id)], cancellationToken))
            .Where(s => s.JudgeMemberId == judgeId)
            .ToDictionary(s => s.EntryId);

        var sheet = new List<JudgingRoundResponse>(rounds.Count);

        foreach (var round in rounds)
        {
            var inRound = theirEntries.Where(e => e.RoundId == round.Id).ToList();
            var hasResult = await roundResults.FindByRoundAsync(round.Id, cancellationToken) is not null;

            sheet.Add(new JudgingRoundResponse(
                round.ToResponse(inRound.Count, hasResult),
                round.Status == RoundStatus.Open,
                [.. inRound.Select(e => new JudgingEntryResponse(
                    e.Id,
                    e.ParticipantMemberId,
                    mine.TryGetValue(e.Id, out var score) ? score.Value : null,
                    mine.TryGetValue(e.Id, out var commented) ? commented.Comments : null))]));
        }

        return Result.Success(new JudgingSheetResponse(
            competition.Id, competition.MaxMarks, judge.Panel.ToString(), sheet));
    }
}

/// <summary>A frozen result, with its totals re-added - see <see cref="CompetitionResult"/>.</summary>
internal static class CompetitionResultDescriber
{
    public static async Task<CompetitionResultResponse> DescribeAsync(
        CompetitionAggregate competition,
        CompetitionResult frozen,
        bool canSeeAll,
        IEntryRepository entries,
        IScoreRepository scores,
        CancellationToken cancellationToken)
    {
        var mainEntries = await entries.ListByRoundsAsync(
            [.. competition.MainRounds.Select(r => r.Id)], cancellationToken);
        var averages = await scores.AverageByEntryAsync([.. mainEntries.Select(e => e.Id)], cancellationToken);
        var totals = CompetitionJudging.Standings(competition, mainEntries, averages)
            .ToDictionary(s => s.MemberId, s => s.Total);

        // The frozen order is the result; ranks follow from it and the totals.
        var standings = CompetitionScoring.Rank(
            frozen.RankedMemberIds.Select((memberId, index) =>
                (memberId, totals.GetValueOrDefault(memberId), (long)index)));

        return new CompetitionResultResponse(
            competition.Id,
            [.. CompetitionScoring.Winners(standings).Select(s => s.ToResponse())],
            canSeeAll ? [.. standings.Select(s => s.ToResponse())] : [],
            competition.RoundCount,
            competition.MaxMarks,
            frozen.PublishedBy,
            frozen.PublishedAt);
    }
}
