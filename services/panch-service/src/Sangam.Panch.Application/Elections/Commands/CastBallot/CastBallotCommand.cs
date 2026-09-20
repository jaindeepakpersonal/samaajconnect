using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Security;
using Sangam.Panch.Domain.Elections;

namespace Sangam.Panch.Application.Elections.Commands.CastBallot;

/// <summary>
/// Casts one member's ballot: up to ten candidates, once.
/// </summary>
/// <remarks>
/// The most contended write path in this service. Three things follow, and none
/// of them is optional.
///
/// <b>The unique index is the guarantee.</b> A ballot is one row unique on
/// <c>(ElectionId, VoterMemberId)</c>, written with all of its votes in a single
/// transaction. Two requests from the same member arriving together both pass any
/// check-then-insert; only the database can refuse the second, and it does. It is
/// also why "at most ten" is a validation on one submission rather than a race
/// between ten inserts.
///
/// <b>The check above it is a courtesy</b>, so the ordinary case - somebody
/// pressing the button twice - gets "you have already voted" rather than a
/// database error.
///
/// <b>The insert is its own transaction</b>, on its own connection, so voters
/// are not serialised by a transaction held open around an election read.
///
/// <b>The ballot is final.</b> A second submission is reported, not applied:
/// letting a member re-vote would make the count depend on when they last
/// changed their mind.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record CastBallotCommand(Guid ElectionId, IReadOnlyList<string> CandidateIds)
    : ICommand<CastBallotResponse>;

/// <summary>
/// <paramref name="Accepted"/> is false when this member had already voted.
/// Reported as success, and <paramref name="Choices"/> says what they hold.
/// </summary>
public sealed record CastBallotResponse(Guid ElectionId, IReadOnlyList<Guid> Choices, bool Accepted);

public sealed class CastBallotCommandValidator : AbstractValidator<CastBallotCommand>
{
    public CastBallotCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();

        // Ids arrive as strings so a malformed one fails here instead of crashing
        // the JSON binder before validation runs.
        RuleFor(x => x.CandidateIds)
            .NotNull()
            .Must(ids => ids is { Count: > 0 })
            .WithMessage("Choose at least one candidate.")
            .Must(ids => ids is null || ids.Count <= PanchElection.SeatCount)
            .WithMessage($"A ballot backs at most {PanchElection.SeatCount} candidates.")
            .Must(ids => ids is null || ids.All(id => Guid.TryParse(id, out _)))
            .WithMessage("Every choice must be a valid candidate id.")
            .Must(ids => ids is null
                || ids.Where(id => Guid.TryParse(id, out _)).Select(Guid.Parse).Distinct().Count() == ids.Count)
            .WithMessage("A candidate can be backed only once on a ballot.");
    }
}

public sealed class CastBallotCommandHandler(
    IElectionRepository elections,
    IBallotRepository ballots,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<CastBallotCommandHandler> logger)
    : IRequestHandler<CastBallotCommand, Result<CastBallotResponse>>
{
    public async Task<Result<CastBallotResponse>> Handle(
        CastBallotCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } voterId)
        {
            return Result.Failure<CastBallotResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null
            || (tenantContext.TenantId is { } tenantId && election.TenantId != tenantId))
        {
            return Result.Failure<CastBallotResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var now = clock.UtcNow;

        if (!election.AcceptsVotes(now))
        {
            return Result.Failure<CastBallotResponse>(Error.Conflict(
                "Election.VotingClosed", "Voting in this election is not open."));
        }

        // Guaranteed parseable and distinct by CastBallotCommandValidator.
        var choices = command.CandidateIds.Select(Guid.Parse).ToList();

        foreach (var choice in choices)
        {
            var candidate = election.FindCandidate(choice);

            if (candidate is null || candidate.Status != CandidateStatus.Approved)
            {
                // Not on the ballot. A nomination nobody approved is not
                // something anyone can vote for.
                return Result.Failure<CastBallotResponse>(
                    Error.NotFound("Candidate.NotFound", "No such candidate on this ballot."));
            }

            if (candidate.MemberId == voterId)
            {
                return Result.Failure<CastBallotResponse>(Error.Conflict(
                    "Vote.Self", "You cannot vote for yourself."));
            }
        }

        // The courtesy check. See the remarks above: this is for the error
        // message, not for correctness.
        var existing = await ballots.FindChoicesAsync(election.Id, voterId, cancellationToken);

        if (existing is not null)
        {
            return Result.Success(new CastBallotResponse(election.Id, existing, false));
        }

        var accepted = await ballots.TryCastAsync(
            new Ballot(election.Id, voterId, now),
            [.. choices.Select(c => new Vote(election.Id, c, voterId, now))],
            cancellationToken);

        if (!accepted)
        {
            // The index refused it: another request from this member won the
            // race. That is the design working, not an error.
            logger.LogInformation(
                "Concurrent ballot refused by the unique index for election {ElectionId}",
                election.Id);

            var theirs = await ballots.FindChoicesAsync(election.Id, voterId, cancellationToken);

            return Result.Success(new CastBallotResponse(election.Id, theirs ?? choices, false));
        }

        return Result.Success(new CastBallotResponse(election.Id, choices, true));
    }
}
