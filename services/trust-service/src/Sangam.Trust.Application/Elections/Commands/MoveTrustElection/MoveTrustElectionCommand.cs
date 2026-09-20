using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Elections;

namespace Sangam.Trust.Application.Elections.Commands.MoveTrustElection;

/// <summary>
/// Opens nominations, opens voting, or closes the election. Strictly
/// forward - the same shape volunteer-groups-service's own
/// <c>MoveElectionCommand</c> already uses. Publishing is its own command,
/// because it has to pick and freeze a winner.
/// </summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record MoveTrustElectionCommand(Guid ElectionId, string Status) : ICommand<TrustElectionResponse>;

public sealed class MoveTrustElectionCommandValidator : AbstractValidator<MoveTrustElectionCommand>
{
    public MoveTrustElectionCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<TrustElectionStatus>(s, ignoreCase: true, out var parsed)
                && parsed != TrustElectionStatus.Published)
            .WithMessage(
                "Status must be NominationsOpen, VotingOpen or Closed. "
                + "Publishing the result is a separate call, because it freezes the winner.");
    }
}

public sealed class MoveTrustElectionCommandHandler(
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<MoveTrustElectionCommand, Result<TrustElectionResponse>>
{
    public async Task<Result<TrustElectionResponse>> Handle(
        MoveTrustElectionCommand command, CancellationToken cancellationToken)
    {
        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null)
        {
            return Result.Failure<TrustElectionResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var target = Enum.Parse<TrustElectionStatus>(command.Status, ignoreCase: true);

        if (target == TrustElectionStatus.VotingOpen && election.Ballot.Count == 0)
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.EmptyBallot",
                "No nominations have been approved, so there is nothing to vote on."));
        }

        var now = clock.UtcNow;

        if (!election.MoveTo(target, now))
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.InvalidTransition",
                $"An election that is {election.Status} cannot become {target}."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(election.ToResponse(now, myVoteCandidateId: null));
    }
}
