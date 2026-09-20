using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;

namespace Sangam.Trust.Application.Elections.Queries;

/// <summary>This Trust's elections, newest first - visible the same way the Trust's own detail is.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListTrustElectionsQuery(Guid TrustId) : IQuery<IReadOnlyList<TrustElectionResponse>>;

public sealed class ListTrustElectionsQueryHandler(
    IElectionRepository elections,
    IVoteRepository votes,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ListTrustElectionsQuery, Result<IReadOnlyList<TrustElectionResponse>>>
{
    public async Task<Result<IReadOnlyList<TrustElectionResponse>>> Handle(
        ListTrustElectionsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<TrustElectionResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var found = await elections.ListForTrustAsync(query.TrustId, cancellationToken);
        var now = clock.UtcNow;
        var results = new List<TrustElectionResponse>(found.Count);

        foreach (var election in found)
        {
            // One targeted lookup per election rather than loading any votes -
            // the same reasoning volunteer-groups-service's own
            // ListElectionsQuery already applies.
            var mine = await votes.FindForVoterAsync(election.Id, memberId, cancellationToken);

            results.Add(election.ToResponse(now, mine?.CandidateId));
        }

        return Result.Success<IReadOnlyList<TrustElectionResponse>>(
            [.. results.OrderByDescending(r => r.CreatedAt)]);
    }
}

/// <summary>One election with its ballot and tally - never hidden.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetTrustElectionQuery(Guid ElectionId) : IQuery<TrustElectionDetailResponse>;

public sealed class GetTrustElectionQueryHandler(
    IElectionRepository elections,
    IVoteRepository votes,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetTrustElectionQuery, Result<TrustElectionDetailResponse>>
{
    public async Task<Result<TrustElectionDetailResponse>> Handle(
        GetTrustElectionQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<TrustElectionDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(query.ElectionId, cancellationToken);

        if (election is null)
        {
            return Result.Failure<TrustElectionDetailResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var tally = await votes.TallyAsync(election.Id, cancellationToken);
        var mine = await votes.FindForVoterAsync(election.Id, memberId, cancellationToken);

        return Result.Success(election.ToDetail(clock.UtcNow, mine?.CandidateId, tally));
    }
}
