using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Elections;
using Sangam.Trust.Domain.Members;

namespace Sangam.Trust.Application.Elections.Commands.PublishTrustElectionResults;

/// <summary>
/// Picks the winners, freezes them, and seats them in the role.
/// </summary>
/// <remarks>
/// Its own command rather than another status move, for the identical
/// reason volunteer-groups-service's own <c>PublishElectionResultsCommand</c>
/// is: it is the one that produces something, and only from
/// <see cref="TrustElectionStatus.Closed"/> - publishing while votes are
/// still arriving would announce an answer to a question still being asked.
///
/// The top <c>Seats</c> by tally win (<see cref="TrustElection.WinnersBy"/>).
/// This writes several aggregates in one handler, one transaction: the
/// <see cref="TrustElection"/> freezes its winners, and each
/// <see cref="TrustMember"/> row is what actually seats them, through the same
/// <c>TrustMember.AssignRole</c> <c>AssignTrustRoleCommand</c> uses - so it
/// raises the same <c>trust.member.updated.v1</c> rather than a second event for
/// the same fact. Whoever held the role before and did not win returns to
/// Trustee: an election replaces a role's holders, the way
/// <c>VolunteerGroup.ElectPosition</c> replaces a same-titled predecessor.
/// </remarks>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record PublishTrustElectionResultsCommand(Guid ElectionId) : ICommand<TrustElectionResponse>;

public sealed class PublishTrustElectionResultsCommandValidator
    : AbstractValidator<PublishTrustElectionResultsCommand>
{
    public PublishTrustElectionResultsCommandValidator() => RuleFor(x => x.ElectionId).NotEmpty();
}

public sealed class PublishTrustElectionResultsCommandHandler(
    ITrustRepository trusts,
    IElectionRepository elections,
    ITrustMemberRepository members,
    IVoteRepository votes,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<PublishTrustElectionResultsCommandHandler> logger)
    : IRequestHandler<PublishTrustElectionResultsCommand, Result<TrustElectionResponse>>
{
    public async Task<Result<TrustElectionResponse>> Handle(
        PublishTrustElectionResultsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<TrustElectionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);
        var trust = election is null ? null : await trusts.GetByIdAsync(election.TrustId, cancellationToken);

        if (election is null || trust is null)
        {
            return Result.Failure<TrustElectionResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var now = clock.UtcNow;

        if (election.Status == TrustElectionStatus.Published)
        {
            // Already announced. The stored winners are the answer, not a
            // fresh computation - the whole point of freezing them. They were
            // seated the first time; publishing again must not do it twice.
            return Result.Success(election.ToResponse(trust, now, []));
        }

        if (election.Status != TrustElectionStatus.Closed)
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.NotClosed",
                "Close the election before publishing its result. Publishing while votes are "
                + "still arriving would announce an answer to a question still being asked."));
        }

        var role = trust.FindRole(election.RoleId);

        if (role is not { Seats: { } seats })
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "TrustRole.NotFound", "The role this election was for no longer exists on the Trust."));
        }

        var tally = await votes.TallyAsync(election.Id, cancellationToken);

        // The role may have lost seats since the election was set up; it seats
        // no more than it has now.
        var winners = election.WinnersBy(tally).Take(seats).ToList();

        if (winners.Count == 0)
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.NoCandidates", "Nobody was on the ballot, so there is no winner to announce."));
        }

        // The election has already refused nominations and votes from anybody
        // not an active member of this Trust, so a winner leaving the roster
        // between voting and publishing is only possible by an admin removing
        // them - and this does not trust that it did not happen.
        var roster = await members.ListByTrustAsync(election.TrustId, cancellationToken);
        var active = roster.Where(m => m.Status == TrustMemberStatus.Active).ToList();
        var winnerIds = winners.Select(w => w.MemberId).ToHashSet();

        if (winnerIds.Any(id => active.All(m => m.MemberId != id)))
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.WinnerNotAMember",
                "A winner is no longer an active member of this Trust. Add them back first."));
        }

        if (!election.Publish([.. winners.Select(w => w.MemberId)], now))
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.NotClosed", "Close the election before publishing its result."));
        }

        // The roster was read untracked; each row that changes is loaded to be written.
        foreach (var outgoing in active.Where(m => m.RoleId == role.Id && !winnerIds.Contains(m.MemberId)))
        {
            var row = await members.GetByIdAsync(outgoing.Id, cancellationToken);

            row?.AssignRole(trust.TrusteeRole.Id, actorId, now);
        }

        foreach (var winner in active.Where(m => winnerIds.Contains(m.MemberId)))
        {
            var row = await members.GetByIdAsync(winner.Id, cancellationToken);

            row?.AssignRole(role.Id, actorId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Election {ElectionId} published for Trust {TrustId}: {Count} elected to role {RoleId}",
            election.Id, election.TrustId, winners.Count, role.Id);

        return Result.Success(election.ToResponse(trust, now, []));
    }
}
