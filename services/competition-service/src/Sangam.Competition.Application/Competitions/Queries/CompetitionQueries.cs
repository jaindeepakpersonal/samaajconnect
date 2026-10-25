using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;
using Sangam.Competition.Domain.Competitions;

namespace Sangam.Competition.Application.Competitions.Queries;

/// <summary>This Samaaj's competitions, newest first, each with the caller's own registration status.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
/// <remarks><paramref name="LocalityId"/> narrows to one locality's competitions - the admin panel's filter.</remarks>
public sealed record ListCompetitionsQuery(Guid? LocalityId = null) : IQuery<IReadOnlyList<CompetitionResponse>>;

public sealed class ListCompetitionsQueryHandler(
    ICompetitionRepository competitions, ICurrentUser currentUser)
    : IRequestHandler<ListCompetitionsQuery, Result<IReadOnlyList<CompetitionResponse>>>
{
    public async Task<Result<IReadOnlyList<CompetitionResponse>>> Handle(
        ListCompetitionsQuery query, CancellationToken cancellationToken)
    {
        var found = await competitions.ListAsync(cancellationToken);

        if (query.LocalityId is { } localityId)
        {
            found = [.. found.Where(c => c.LocalityId == localityId)];
        }

        return Result.Success<IReadOnlyList<CompetitionResponse>>(
            [.. found.Select(c => c.ToResponse(currentUser.UserId))]);
    }
}

/// <summary>One competition: its rounds, registration count, judges, and this caller's own status.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetCompetitionQuery(Guid CompetitionId) : IQuery<CompetitionDetailResponse>;

public sealed class GetCompetitionQueryHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IRoundResultRepository roundResults,
    ICompetitionResultRepository results,
    ICurrentUser currentUser)
    : IRequestHandler<GetCompetitionQuery, Result<CompetitionDetailResponse>>
{
    public async Task<Result<CompetitionDetailResponse>> Handle(
        GetCompetitionQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(query.CompetitionId, cancellationToken);

        if (competition is null)
        {
            return Result.Failure<CompetitionDetailResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var rounds = new List<RoundResponse>(competition.Rounds.Count);

        foreach (var round in competition.Rounds.OrderBy(r => r.SequenceNumber))
        {
            var entryCount = await entries.CountByRoundAsync(round.Id, cancellationToken);
            var hasResult = await roundResults.FindByRoundAsync(round.Id, cancellationToken) is not null;

            rounds.Add(round.ToResponse(entryCount, hasResult));
        }

        var canManage = currentUser.HasPermission(PermissionKeys.CompetitionManage);

        var registeredMemberIds = competition.Registrations
            .Where(r => r.Status == RegistrationStatus.Registered)
            .OrderBy(r => r.RegisteredAt)
            .Select(r => r.MemberId)
            .ToList();

        return Result.Success(new CompetitionDetailResponse(
            competition.ToResponse(currentUser.UserId),
            rounds,
            [.. competition.Judges.OrderBy(j => j.AssignedAt).Select(j => j.ToResponse())],
            registeredMemberIds,
            canManage,
            await results.FindByCompetitionAsync(competition.Id, cancellationToken) is not null));
    }
}

/// <summary>
/// One round's entries. Per-entry marks are visible only to the organiser or
/// an assigned judge - never to an ordinary member, before or after
/// publication.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetRoundQuery(Guid CompetitionId, Guid RoundId) : IQuery<RoundDetailResponse>;

public sealed class GetRoundQueryHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IScoreRepository scores,
    IRoundResultRepository roundResults,
    ICurrentUser currentUser)
    : IRequestHandler<GetRoundQuery, Result<RoundDetailResponse>>
{
    public async Task<Result<RoundDetailResponse>> Handle(
        GetRoundQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(query.CompetitionId, cancellationToken);

        if (competition is null)
        {
            return Result.Failure<RoundDetailResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var round = competition.FindRound(query.RoundId);

        if (round is null)
        {
            return Result.Failure<RoundDetailResponse>(
                Error.NotFound("Round.NotFound", "No such round in this competition."));
        }

        var canManage = currentUser.HasPermission(PermissionKeys.CompetitionManage);
        var isJudge = currentUser.UserId is { } judgeId && competition.IsJudge(judgeId);
        var canSeeScores = canManage || isJudge;

        var roundEntries = await entries.ListByRoundAsync(round.Id, cancellationToken);
        var entryIds = roundEntries.Select(e => e.Id).ToList();

        // Fetched only when this caller may see it - fetching and then
        // discarding would work, and would be one refactor away from leaking.
        var averages = canSeeScores
            ? await scores.AverageByEntryAsync(entryIds, cancellationToken)
            : null;

        var mine = isJudge && currentUser.UserId is { } myId
            ? await scores.ListMineAsync(entryIds, myId, cancellationToken)
            : null;

        var entryResponses = roundEntries.Select(e => new EntryResponse(
                e.Id,
                e.ParticipantMemberId,
                e.CreatedAt,
                averages is not null && averages.TryGetValue(e.Id, out var avg) ? avg : null,
                mine is not null && mine.TryGetValue(e.Id, out var myScore) ? myScore : null))
            .ToList();

        var hasResult = await roundResults.FindByRoundAsync(round.Id, cancellationToken) is not null;

        return Result.Success(new RoundDetailResponse(
            round.ToResponse(roundEntries.Count, hasResult), entryResponses, canSeeScores));
    }
}

/// <summary>A round's published ranking. 404 until it has one.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetRoundResultQuery(Guid CompetitionId, Guid RoundId) : IQuery<RoundResultResponse>;

public sealed class GetRoundResultQueryHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IScoreRepository scores,
    IRoundResultRepository roundResults)
    : IRequestHandler<GetRoundResultQuery, Result<RoundResultResponse>>
{
    public async Task<Result<RoundResultResponse>> Handle(
        GetRoundResultQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(query.CompetitionId, cancellationToken);

        if (competition is null || competition.FindRound(query.RoundId) is null)
        {
            return Result.Failure<RoundResultResponse>(
                Error.NotFound("Round.NotFound", "No such round in this competition."));
        }

        var result = await roundResults.FindByRoundAsync(query.RoundId, cancellationToken);

        if (result is null)
        {
            return Result.Failure<RoundResultResponse>(Error.NotFound(
                "Round.NotPublished", "This round has no published result."));
        }

        var roundEntries = await entries.ListByRoundAsync(query.RoundId, cancellationToken);
        var byId = roundEntries.ToDictionary(e => e.Id);
        var averages = await scores.AverageByEntryAsync(result.RankedEntryIds, cancellationToken);

        var ranking = result.RankedEntryIds
            .Select((entryId, index) => new RoundResultEntry(
                index + 1,
                entryId,
                byId.TryGetValue(entryId, out var e) ? e.ParticipantMemberId : Guid.Empty,
                averages.GetValueOrDefault(entryId)))
            .ToList();

        return Result.Success(new RoundResultResponse(
            result.RoundId, ranking, result.PublishedBy, result.PublishedAt));
    }
}
