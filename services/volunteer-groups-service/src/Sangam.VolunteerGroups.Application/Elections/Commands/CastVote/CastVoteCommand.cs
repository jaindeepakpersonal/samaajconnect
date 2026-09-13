using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Application.Elections.Commands.CastVote;

/// <summary>
/// Casts one group member's one vote in one election.
/// </summary>
/// <remarks>
/// The identical guarantee celebrity-voting-service's own <c>CastVoteCommand</c>
/// documents in detail applies here at a smaller scale: the unique index on
/// <c>(ElectionId, VoterMemberId)</c> is what actually prevents a double vote,
/// not the courtesy check below it, and the insert happens on its own scope
/// so voters are never serialised against each other by a transaction held
/// open around an election read. See <c>IVoteRepository.TryCastAsync</c>.
///
/// Self-votes are refused, matching celebrity-voting-service's own rule -
/// keeping the platform's two election systems consistent.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record CastVoteCommand(Guid ElectionId, Guid CandidateId) : ICommand<CastVoteResponse>;

/// <summary>
/// <paramref name="Accepted"/> is false when this member had already voted.
/// Reported as success: pressing the button twice is not misconduct, and the
/// response carries the vote they hold either way.
/// </summary>
public sealed record CastVoteResponse(Guid ElectionId, Guid CandidateId, bool Accepted);

public sealed class CastVoteCommandValidator : AbstractValidator<CastVoteCommand>
{
    public CastVoteCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
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

        var candidate = election.FindCandidate(command.CandidateId);

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

        // The courtesy check. Not the guarantee - see the remarks above.
        var existing = await votes.FindForVoterAsync(election.Id, voterId, cancellationToken);

        if (existing is not null)
        {
            return Result.Success(new CastVoteResponse(election.Id, existing.CandidateId, false));
        }

        var accepted = await votes.TryCastAsync(
            new Vote(election.Id, candidate.Id, voterId, now), cancellationToken);

        if (!accepted)
        {
            logger.LogInformation(
                "Concurrent vote refused by the unique index for election {ElectionId}",
                election.Id);

            var theirs = await votes.FindForVoterAsync(election.Id, voterId, cancellationToken);

            return Result.Success(new CastVoteResponse(
                election.Id, theirs?.CandidateId ?? candidate.Id, false));
        }

        return Result.Success(new CastVoteResponse(election.Id, candidate.Id, true));
    }
}
