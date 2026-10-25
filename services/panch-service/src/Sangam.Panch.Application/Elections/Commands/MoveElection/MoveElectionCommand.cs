using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Security;
using Sangam.Panch.Domain.Elections;

namespace Sangam.Panch.Application.Elections.Commands.MoveElection;

/// <summary>
/// Opens nominations, opens voting, or closes the election.
/// </summary>
/// <remarks>
/// Strictly forward: Draft → NominationsOpen → VotingOpen → Closed. An election
/// that can go backwards is not an election, so there is no route back and no
/// command to reopen one. Publishing the result is its own command, because it
/// has to compute and freeze the ranking.
/// </remarks>
[RequiresPermission(PermissionKeys.PanchManage)]
public sealed record MoveElectionCommand(Guid ElectionId, string Status)
    : ICommand<ElectionResponse>;

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
                + "Publishing the result is a separate call, because it freezes the ranking.");
    }
}

public sealed class MoveElectionCommandHandler(
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<MoveElectionCommandHandler> logger)
    : IRequestHandler<MoveElectionCommand, Result<ElectionResponse>>
{
    public async Task<Result<ElectionResponse>> Handle(
        MoveElectionCommand command,
        CancellationToken cancellationToken)
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

        var target = Enum.Parse<ElectionStatus>(command.Status, ignoreCase: true);

        if (target == ElectionStatus.VotingOpen && election.Ballot.Count == 0)
        {
            // A ballot with nobody on it is a vote nobody can cast, and the
            // first anyone would learn of it is a member trying.
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.EmptyBallot",
                "No nominations have been approved, so there is nothing to vote on."));
        }

        if (!election.MoveTo(target, actorId, clock.UtcNow))
        {
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.InvalidTransition",
                $"An election that is {election.Status} cannot become {target}."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Election {ElectionId} moved to {Status}", election.Id, target);

        return Result.Success(election.ToResponse(clock.UtcNow, myChoices: null));
    }
}
