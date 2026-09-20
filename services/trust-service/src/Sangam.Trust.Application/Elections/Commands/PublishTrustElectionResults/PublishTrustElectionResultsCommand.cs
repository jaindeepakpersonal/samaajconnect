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
/// Picks the winner, freezes it, and installs them in the position.
/// </summary>
/// <remarks>
/// Its own command rather than another status move, for the identical
/// reason volunteer-groups-service's own <c>PublishElectionResultsCommand</c>
/// is: it is the one that produces something, and only from
/// <see cref="TrustElectionStatus.Closed"/> - publishing while votes are
/// still arriving would announce an answer to a question still being asked.
///
/// This writes two aggregates in one handler, one transaction: the
/// <see cref="TrustElection"/> freezes its own winner, and the winner's own
/// <see cref="TrustMember"/> row is what actually installs them, by calling
/// the identical <c>UpdatePosition</c> method <c>UpdateTrustMemberCommand</c>
/// already uses - so this raises the same <c>TrustMemberUpdatedDomainEvent</c>
/// audit-notification-service already has a descriptor for, rather than a
/// second event for the same fact. Any other active member of this Trust
/// currently holding the same title has it cleared the same way, mirroring
/// volunteer-groups-service's own <c>VolunteerGroup.ElectPosition</c> clearing
/// a same-titled predecessor - a position has one holder.
/// </remarks>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record PublishTrustElectionResultsCommand(Guid ElectionId) : ICommand<TrustElectionResponse>;

public sealed class PublishTrustElectionResultsCommandValidator
    : AbstractValidator<PublishTrustElectionResultsCommand>
{
    public PublishTrustElectionResultsCommandValidator() => RuleFor(x => x.ElectionId).NotEmpty();
}

public sealed class PublishTrustElectionResultsCommandHandler(
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

        if (election is null)
        {
            return Result.Failure<TrustElectionResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var now = clock.UtcNow;

        if (election.Status == TrustElectionStatus.Published)
        {
            // Already announced. The stored winner is the answer, not a
            // fresh computation - the whole point of freezing it. The
            // position was installed the first time; publishing again must
            // not clear and reinstall it a second time.
            return Result.Success(election.ToResponse(now, myVoteCandidateId: null));
        }

        if (election.Status != TrustElectionStatus.Closed)
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.NotClosed",
                "Close the election before publishing its result. Publishing while votes are "
                + "still arriving would announce an answer to a question still being asked."));
        }

        var tally = await votes.TallyAsync(election.Id, cancellationToken);
        var winner = election.WinnerBy(tally);

        if (winner is null)
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.NoCandidates", "Nobody was on the ballot, so there is no winner to announce."));
        }

        if (!election.Publish(winner.MemberId, now))
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.NotClosed", "Close the election before publishing its result."));
        }

        // The election has already refused nominations and votes from anybody
        // not an active member of this Trust, so the winner leaving the
        // roster between voting and publishing is not possible through this
        // service's own API - but this does not trust that promise from the
        // other side of the call.
        var winnerMember = await members.FindAsync(election.TrustId, winner.MemberId, cancellationToken);

        if (winnerMember is null || winnerMember.Status != TrustMemberStatus.Active)
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.WinnerNotAMember",
                "The winner is no longer an active member of this Trust."));
        }

        var roster = await members.ListByTrustAsync(election.TrustId, cancellationToken);

        var previousHolderIds = roster
            .Where(m => m.Id != winnerMember.Id
                && m.Status == TrustMemberStatus.Active
                && string.Equals(m.PositionTitle, election.PositionTitle, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Id)
            .ToArray();

        foreach (var previousHolderId in previousHolderIds)
        {
            var previousHolder = await members.GetByIdAsync(previousHolderId, cancellationToken);

            previousHolder?.UpdatePosition(null, actorId, now);
        }

        winnerMember.UpdatePosition(election.PositionTitle, actorId, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Election {ElectionId} published for Trust {TrustId}: {WinnerMemberId} elected {PositionTitle}",
            election.Id, election.TrustId, winner.MemberId, election.PositionTitle);

        return Result.Success(election.ToResponse(now, myVoteCandidateId: null));
    }
}
