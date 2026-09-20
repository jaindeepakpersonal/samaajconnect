using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Elections;
using Sangam.Trust.Domain.Members;

namespace Sangam.Trust.Application.Elections.Commands.CastTrustVote;

/// <summary>
/// Casts one Trust member's one vote in one election.
/// </summary>
/// <remarks>
/// The identical guarantee volunteer-groups-service's own <c>CastVoteCommand</c>
/// documents applies here: the unique index on <c>(ElectionId,
/// VoterMemberId)</c> is what actually prevents a double vote, not the
/// courtesy check below it, and the insert happens on its own scope so
/// voters are never serialised against each other by a transaction held open
/// around an election read. See <c>IVoteRepository.TryCastAsync</c>.
///
/// Self-votes are refused, matching the platform-wide rule already settled
/// by celebrity-voting-service, panch-service and volunteer-groups-service.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record CastTrustVoteCommand(Guid ElectionId, Guid CandidateId) : ICommand<CastTrustVoteResponse>;

/// <summary>
/// <paramref name="Accepted"/> is false when this member had already voted.
/// Reported as success: pressing the button twice is not misconduct, and the
/// response carries the vote they hold either way.
/// </summary>
public sealed record CastTrustVoteResponse(Guid ElectionId, Guid CandidateId, bool Accepted);

public sealed class CastTrustVoteCommandValidator : AbstractValidator<CastTrustVoteCommand>
{
    public CastTrustVoteCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
    }
}

public sealed class CastTrustVoteCommandHandler(
    IElectionRepository elections,
    ITrustMemberRepository members,
    IVoteRepository votes,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<CastTrustVoteCommandHandler> logger)
    : IRequestHandler<CastTrustVoteCommand, Result<CastTrustVoteResponse>>
{
    public async Task<Result<CastTrustVoteResponse>> Handle(
        CastTrustVoteCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } voterId)
        {
            return Result.Failure<CastTrustVoteResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null)
        {
            return Result.Failure<CastTrustVoteResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var voter = await members.FindAsync(election.TrustId, voterId, cancellationToken);

        if (voter is null || voter.Status != TrustMemberStatus.Active)
        {
            return Result.Failure<CastTrustVoteResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var now = clock.UtcNow;

        if (!election.AcceptsVotes(now))
        {
            return Result.Failure<CastTrustVoteResponse>(Error.Conflict(
                "Election.VotingClosed", "Voting in this election is not open."));
        }

        var candidate = election.FindCandidate(command.CandidateId);

        if (candidate is null || candidate.Status != TrustCandidateStatus.Approved)
        {
            return Result.Failure<CastTrustVoteResponse>(
                Error.NotFound("Candidate.NotFound", "No such candidate on this ballot."));
        }

        if (candidate.MemberId == voterId)
        {
            return Result.Failure<CastTrustVoteResponse>(Error.Conflict(
                "Vote.Self", "You cannot vote for yourself."));
        }

        // The courtesy check. Not the guarantee - see the remarks above.
        var existing = await votes.FindForVoterAsync(election.Id, voterId, cancellationToken);

        if (existing is not null)
        {
            return Result.Success(new CastTrustVoteResponse(election.Id, existing.CandidateId, false));
        }

        var accepted = await votes.TryCastAsync(
            new Vote(election.Id, candidate.Id, voterId, now), cancellationToken);

        if (!accepted)
        {
            logger.LogInformation(
                "Concurrent vote refused by the unique index for election {ElectionId}",
                election.Id);

            var theirs = await votes.FindForVoterAsync(election.Id, voterId, cancellationToken);

            return Result.Success(new CastTrustVoteResponse(
                election.Id, theirs?.CandidateId ?? candidate.Id, false));
        }

        return Result.Success(new CastTrustVoteResponse(election.Id, candidate.Id, true));
    }
}
