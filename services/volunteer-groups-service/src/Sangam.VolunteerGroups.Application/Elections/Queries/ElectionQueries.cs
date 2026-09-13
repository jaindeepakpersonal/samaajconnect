using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Elections.Queries;

/// <summary>This group's elections, newest first - visible the same way the group's own detail is.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListElectionsQuery(Guid GroupId) : IQuery<IReadOnlyList<ElectionResponse>>;

public sealed class ListElectionsQueryHandler(
    IElectionRepository elections,
    IVoteRepository votes,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ListElectionsQuery, Result<IReadOnlyList<ElectionResponse>>>
{
    public async Task<Result<IReadOnlyList<ElectionResponse>>> Handle(
        ListElectionsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<ElectionResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var found = await elections.ListForGroupAsync(query.GroupId, cancellationToken);
        var now = clock.UtcNow;
        var results = new List<ElectionResponse>(found.Count);

        foreach (var election in found)
        {
            // One targeted lookup per election rather than loading any votes -
            // the same reasoning celebrity-voting-service's own
            // ListCampaignsQuery already applies.
            var mine = await votes.FindForVoterAsync(election.Id, memberId, cancellationToken);

            results.Add(election.ToResponse(now, mine?.CandidateId));
        }

        return Result.Success<IReadOnlyList<ElectionResponse>>(
            [.. results.OrderByDescending(r => r.CreatedAt)]);
    }
}

/// <summary>One election with its ballot and tally - never hidden, unlike celebrity-voting's own campaigns.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetElectionQuery(Guid ElectionId) : IQuery<ElectionDetailResponse>;

public sealed class GetElectionQueryHandler(
    IElectionRepository elections,
    IVoteRepository votes,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetElectionQuery, Result<ElectionDetailResponse>>
{
    public async Task<Result<ElectionDetailResponse>> Handle(
        GetElectionQuery query, CancellationToken cancellationToken)
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

        var tally = await votes.TallyAsync(election.Id, cancellationToken);
        var mine = await votes.FindForVoterAsync(election.Id, memberId, cancellationToken);

        return Result.Success(election.ToDetail(clock.UtcNow, mine?.CandidateId, tally));
    }
}
