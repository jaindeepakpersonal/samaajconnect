using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Application.Elections.Commands.MoveElection;

/// <summary>
/// Opens nominations, opens voting, or closes the election. Strictly
/// forward - the same shape and the same reasoning as celebrity-voting-
/// service's own <c>MoveCampaignCommand</c>. Publishing is its own command,
/// because it has to pick and freeze a winner.
/// </summary>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record MoveElectionCommand(Guid ElectionId, string Status) : ICommand<ElectionResponse>;

public sealed class MoveElectionCommandValidator : AbstractValidator<MoveElectionCommand>
{
    public MoveElectionCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<ElectionStatus>(s, ignoreCase: true, out var parsed)
                && parsed != ElectionStatus.Published)
            .WithMessage(
                "Status must be NominationsOpen, VotingOpen or Closed. "
                + "Publishing the result is a separate call, because it freezes the winner.");
    }
}

public sealed class MoveElectionCommandHandler(
    IGroupRepository groups,
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<MoveElectionCommand, Result<ElectionResponse>>
{
    public async Task<Result<ElectionResponse>> Handle(
        MoveElectionCommand command, CancellationToken cancellationToken)
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

        if (group is null || !group.IsPresident(actorId))
        {
            return Result.Failure<ElectionResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var target = Enum.Parse<ElectionStatus>(command.Status, ignoreCase: true);

        if (target == ElectionStatus.VotingOpen && election.Ballot.Count == 0)
        {
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.EmptyBallot",
                "No nominations have been approved, so there is nothing to vote on."));
        }

        var now = clock.UtcNow;

        if (!election.MoveTo(target, now))
        {
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.InvalidTransition",
                $"An election that is {election.Status} cannot become {target}."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(election.ToResponse(now, myVoteCandidateId: null));
    }
}
