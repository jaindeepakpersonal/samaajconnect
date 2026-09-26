using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Application.Elections.Commands.CastVote;

/// <summary>
/// Casts one group member's one ballot in one election: up to as many
/// candidates as the role has seats, all counted the same.
/// </summary>
/// <remarks>
/// The identical guarantee celebrity-voting-service's own <c>CastVoteCommand</c>
/// documents in detail applies here at a smaller scale: database indexes are
/// what actually prevent a second ballot, not the courtesy check below, and the
/// insert happens on its own scope so voters are never serialised against each
/// other by a transaction held open around an election read. A ballot's picks
/// are numbered from 1 and written together, so a second ballot always
/// collides on its first pick. See <c>Vote</c> and
/// <c>IVoteRepository.TryCastAsync</c>.
///
/// <b>A ballot is final</b>, like a Panch ballot: picking two of three
/// Coordinators and coming back for the third is a second ballot, refused. The
/// screen says so before the button is pressed.
///
/// Self-votes are refused, matching celebrity-voting-service's own rule -
/// keeping the platform's election systems consistent.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record CastVoteCommand(Guid ElectionId, IReadOnlyList<Guid> CandidateIds)
    : ICommand<CastVoteResponse>;

/// <summary>
/// <paramref name="Accepted"/> is false when this member had already voted.
/// Reported as success: pressing the button twice is not misconduct, and the
/// response carries the ballot they hold either way.
/// </summary>
public sealed record CastVoteResponse(Guid ElectionId, IReadOnlyList<Guid> CandidateIds, bool Accepted);

public sealed class CastVoteCommandValidator : AbstractValidator<CastVoteCommand>
{
    public CastVoteCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();

        RuleFor(x => x.CandidateIds)
            .NotEmpty()
            .WithMessage("Pick at least one candidate.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Pick each candidate once.")
            .Must(ids => ids.Count <= 100);

        RuleForEach(x => x.CandidateIds).NotEmpty();
    }
}

public sealed class CastVoteCommandHandler(
    IGroupRepository groups,
    IElectionRepository elections,
    IVoteRepository votes,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<CastVoteCommandHandler> logger)
    : IRequestHandler<CastVoteCommand, Result<CastVoteResponse>>
{
    public async Task<Result<CastVoteResponse>> Handle(
        CastVoteCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } voterId)
        {
            return Result.Failure<CastVoteResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null
            || (tenantContext.TenantId is { } tenantId && election.TenantId != tenantId))
        {
            return Result.Failure<CastVoteResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var group = await groups.GetByIdAsync(election.GroupId, cancellationToken);

        if (group is null || !group.HasMember(voterId))
        {
            return Result.Failure<CastVoteResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var now = clock.UtcNow;

        if (!election.AcceptsVotes(now))
        {
            return Result.Failure<CastVoteResponse>(Error.Conflict(
                "Election.VotingClosed", "Voting in this election is not open."));
        }

        if (command.CandidateIds.Count > election.Seats)
        {
            return Result.Failure<CastVoteResponse>(Error.Validation(new Dictionary<string, string[]>
            {
                [nameof(CastVoteCommand.CandidateIds)] =
                [
                    election.Seats == 1
                        ? "Pick one candidate."
                        : $"Pick at most {election.Seats} candidates - that is how many seats this role has.",
                ],
            }));
        }

        var picked = new List<ElectionCandidate>(command.CandidateIds.Count);

        foreach (var candidateId in command.CandidateIds)
        {
            var candidate = election.FindCandidate(candidateId);

            if (candidate is null || candidate.Status != CandidateStatus.Approved)
            {
                return Result.Failure<CastVoteResponse>(
                    Error.NotFound("Candidate.NotFound", "No such candidate on this ballot."));
            }

            if (candidate.MemberId == voterId)
            {
                return Result.Failure<CastVoteResponse>(Error.Conflict(
                    "Vote.Self", "You cannot vote for yourself."));
            }

            picked.Add(candidate);
        }

        // The courtesy check. Not the guarantee - see the remarks above.
        var existing = await votes.FindForVoterAsync(election.Id, voterId, cancellationToken);

        if (existing.Count > 0)
        {
            return Result.Success(new CastVoteResponse(
                election.Id, [.. existing.Select(v => v.CandidateId)], false));
        }

        var ballot = picked
            .Select((candidate, index) => new Vote(election.Id, candidate.Id, voterId, index + 1, now))
            .ToList();

        if (!await votes.TryCastAsync(ballot, cancellationToken))
        {
            logger.LogInformation(
                "Concurrent ballot refused by the unique index for election {ElectionId}",
                election.Id);

            var theirs = await votes.FindForVoterAsync(election.Id, voterId, cancellationToken);

            return Result.Success(new CastVoteResponse(
                election.Id, [.. theirs.Select(v => v.CandidateId)], false));
        }

        return Result.Success(new CastVoteResponse(election.Id, [.. picked.Select(c => c.Id)], true));
    }
}
