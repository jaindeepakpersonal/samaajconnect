using MediatR;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Security;

namespace Sangam.Panch.Application.Elections.Queries;

/// <summary>This Samaaj's elections, newest first.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListElectionsQuery : IQuery<IReadOnlyList<ElectionResponse>>;

public sealed class ListElectionsQueryHandler(
    IElectionRepository elections,
    IBallotRepository ballots,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ListElectionsQuery, Result<IReadOnlyList<ElectionResponse>>>
{
    public async Task<Result<IReadOnlyList<ElectionResponse>>> Handle(
        ListElectionsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<ElectionResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var found = await elections.ListAsync(cancellationToken);
        var now = clock.UtcNow;
        var results = new List<ElectionResponse>(found.Count);

        foreach (var election in found)
        {
            // One targeted lookup per election rather than loading any ballots.
            // A Samaaj has a handful of elections and thousands of ballots.
            var mine = await ballots.FindChoicesAsync(election.Id, memberId, cancellationToken);

            results.Add(election.ToResponse(now, mine));
        }

        return Result.Success<IReadOnlyList<ElectionResponse>>(results);
    }
}

/// <summary>
/// One election with its ballot, and the tally when this caller may see it. An
/// election set to HiddenUntilClose shows a member the names and no numbers
/// until voting is over; an administrator sees the numbers throughout, because
/// somebody has to be able to tell whether the thing is working.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetElectionQuery(Guid ElectionId) : IQuery<ElectionDetailResponse>;

public sealed class GetElectionQueryHandler(
    IElectionRepository elections,
    IBallotRepository ballots,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetElectionQuery, Result<ElectionDetailResponse>>
{
    public async Task<Result<ElectionDetailResponse>> Handle(
        GetElectionQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<ElectionDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(query.ElectionId, cancellationToken);

        if (election is null)
        {
            return Result.Failure<ElectionDetailResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var canAdminister = currentUser.HasPermission(PermissionKeys.PanchManage);

        // Fetched only when this caller may see it.
        var tally = election.TallyVisibleTo(canAdminister)
            ? await ballots.TallyAsync(election.Id, cancellationToken)
            : null;

        var mine = await ballots.FindChoicesAsync(election.Id, memberId, cancellationToken);

        return Result.Success(election.ToDetail(clock.UtcNow, mine, canAdminister, tally));
    }
}

/// <summary>The published result: the frozen order, with the counts behind it.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetResultsQuery(Guid ElectionId) : IQuery<ElectionResultResponse>;

public sealed class GetResultsQueryHandler(
    IElectionRepository elections, IBallotRepository ballots)
    : IRequestHandler<GetResultsQuery, Result<ElectionResultResponse>>
{
    public async Task<Result<ElectionResultResponse>> Handle(
        GetResultsQuery query,
        CancellationToken cancellationToken)
    {
        var election = await elections.GetByIdAsync(query.ElectionId, cancellationToken);

        if (election is null)
        {
            return Result.Failure<ElectionResultResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var result = await ballots.FindResultAsync(election.Id, cancellationToken);

        if (result is null)
        {
            return Result.Failure<ElectionResultResponse>(Error.NotFound(
                "Election.NotPublished", "This election has no published result."));
        }

        var tally = await ballots.TallyAsync(election.Id, cancellationToken);

        return Result.Success(new ElectionResultResponse(
            election.Id,
            [
                .. result.RankedCandidateIds.Select((candidateId, index) => new ResultEntry(
                    index + 1,
                    candidateId,
                    election.FindCandidate(candidateId)?.MemberId ?? Guid.Empty,
                    tally.GetValueOrDefault(candidateId)))
            ],
            result.SeatedMemberIds,
            result.PublishedBy,
            result.PublishedAt));
    }
}
