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
/// Casts one Trust member's one ballot in one election: up to as many
/// candidates as the role has seats, each at most once.
/// </summary>
/// <remarks>
/// The identical guarantee volunteer-groups-service's own <c>CastVoteCommand</c>
/// documents applies here: the unique indexes on <c>(ElectionId,
/// VoterMemberId, Choice)</c> and <c>(ElectionId, VoterMemberId,
/// CandidateId)</c> are what actually prevent a second ballot, not the
/// courtesy check below them, and the ballot is inserted on its own scope so
/// voters are never serialised against each other by a transaction held open
/// around an election read. See <c>IVoteRepository.TryCastAsync</c>.
///
/// Self-votes are refused, matching the platform-wide rule already settled
/// by celebrity-voting-service, panch-service and volunteer-groups-service.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record CastTrustVoteCommand(Guid ElectionId, IReadOnlyList<Guid> CandidateIds)
    : ICommand<CastTrustVoteResponse>;

/// <summary>
/// <paramref name="Accepted"/> is false when this member had already voted.
/// Reported as success: pressing the button twice is not misconduct, and the
/// response carries the ballot they hold either way.
/// </summary>
public sealed record CastTrustVoteResponse(Guid ElectionId, IReadOnlyList<Guid> CandidateIds, bool Accepted);

public sealed class CastTrustVoteCommandValidator : AbstractValidator<CastTrustVoteCommand>
{
    public CastTrustVoteCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();

        RuleFor(x => x.CandidateIds)
            .NotEmpty()
            .WithMessage("Pick at least one candidate.")
            .Must(ids => ids.Count <= 50)
            .WithMessage("A ballot has at most 50 picks.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Pick each candidate at most once.");

        RuleForEach(x => x.CandidateIds).NotEmpty();
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

        if (command.CandidateIds.Count > election.Seats)
        {
            return Result.Failure<CastTrustVoteResponse>(Error.Conflict(
                "Vote.TooMany",
                election.Seats == 1
                    ? "Pick one candidate."
                    : $"Pick at most {election.Seats} candidates - one for each seat."));
        }

        var picked = new List<TrustElectionCandidate>(command.CandidateIds.Count);

        foreach (var candidateId in command.CandidateIds)
        {
            var candidate = election.FindCandidate(candidateId);

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

            picked.Add(candidate);
        }

        // The courtesy check. Not the guarantee - see the remarks above.
        var existing = await votes.FindForVoterAsync(election.Id, voterId, cancellationToken);

        if (existing.Count > 0)
        {
            return Result.Success(new CastTrustVoteResponse(election.Id, existing, false));
        }

        var ballot = picked
            .Select((candidate, index) => new Vote(election.Id, candidate.Id, voterId, index + 1, now))
            .ToList();

        var accepted = await votes.TryCastAsync(ballot, cancellationToken);

        if (!accepted)
        {
            logger.LogInformation(
                "Concurrent ballot refused by the unique index for election {ElectionId}",
                election.Id);

            var theirs = await votes.FindForVoterAsync(election.Id, voterId, cancellationToken);

            return Result.Success(new CastTrustVoteResponse(election.Id, theirs, false));
        }

        return Result.Success(new CastTrustVoteResponse(election.Id, [.. picked.Select(c => c.Id)], true));
    }
}
