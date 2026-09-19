using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Groups;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Application.Elections.Commands.PublishElectionResults;

/// <summary>
/// Picks the winner, freezes it, and installs them in the position.
/// </summary>
/// <remarks>
/// Its own command rather than another status move, for the identical reason
/// celebrity-voting-service's own <c>PublishResultsCommand</c> is: it is the
/// one that produces something, and only from <see cref="ElectionStatus.Closed"/> -
/// publishing while votes are still arriving would announce an answer to a
/// question still being asked.
///
/// This is the one command in this service that writes to two aggregates:
/// the <see cref="GroupElection"/> freezes its own winner, and the owning
/// <c>VolunteerGroup</c> is what actually installs them via
/// <c>VolunteerGroup.ElectPosition</c> - the same "two aggregates, one
/// transaction, one handler" shape businesses-service's own <c>Order</c>
/// handler already uses when it touches a <c>Business</c>. A published
/// election is what actually installs someone in the position, not merely an
/// announcement (confirmed with the user before this was built).
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record PublishElectionResultsCommand(Guid ElectionId) : ICommand<ElectionResponse>;

public sealed class PublishElectionResultsCommandValidator
    : AbstractValidator<PublishElectionResultsCommand>
{
    public PublishElectionResultsCommandValidator() => RuleFor(x => x.ElectionId).NotEmpty();
}

public sealed class PublishElectionResultsCommandHandler(
    IGroupRepository groups,
    IElectionRepository elections,
    IVoteRepository votes,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<PublishElectionResultsCommandHandler> logger)
    : IRequestHandler<PublishElectionResultsCommand, Result<ElectionResponse>>
{
    public async Task<Result<ElectionResponse>> Handle(
        PublishElectionResultsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<ElectionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null
            || (tenantContext.TenantId is { } tenantId && election.TenantId != tenantId))
        {
            return Result.Failure<ElectionResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var group = await groups.GetByIdAsync(election.GroupId, cancellationToken);

        if (group is null || !group.CanRunElection(actorId, currentUser.IsGroupAdmin(), election.PositionTitle))
        {
            return Result.Failure<ElectionResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var now = clock.UtcNow;

        if (election.Status == ElectionStatus.Published)
        {
            // Already announced. The stored winner is the answer, not a
            // fresh computation - the whole point of freezing it.
            return Result.Success(election.ToResponse(now, myVoteCandidateId: null));
        }

        if (election.Status != ElectionStatus.Closed)
        {
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.NotClosed",
                "Close the election before publishing its result. Publishing while votes are "
                + "still arriving would announce an answer to a question still being asked."));
        }

        var tally = await votes.TallyAsync(election.Id, cancellationToken);
        var winner = election.WinnerBy(tally);

        if (winner is null)
        {
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.NoCandidates", "Nobody was on the ballot, so there is no winner to announce."));
        }

        if (!election.Publish(winner.MemberId, now))
        {
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.NotClosed", "Close the election before publishing its result."));
        }

        // The election has already refused this if the winner somehow left
        // the group between voting and publishing is not possible here since
        // Nominate/CastVote both check membership - but ElectPosition does
        // not trust that promise from the other side of the call, and
        // returning false would mean a data inconsistency worth knowing
        // about rather than silently swallowed.
        group.ElectPosition(election.PositionTitle, winner.MemberId, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Election {ElectionId} published for group {GroupId}: {WinnerMemberId} elected {PositionTitle}",
            election.Id, election.GroupId, winner.MemberId, election.PositionTitle);

        return Result.Success(election.ToResponse(now, myVoteCandidateId: null));
    }
}
