using FluentValidation;
using MediatR;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Security;
using Sangam.Panch.Domain.Elections;

namespace Sangam.Panch.Application.Elections.Commands.ExtendVoting;

/// <summary>
/// Keeps voting open longer, while it is open. Later only - see
/// <see cref="PanchElection.ExtendVoting"/> for why it never moves earlier.
/// </summary>
[RequiresPermission(PermissionKeys.PanchManage)]
public sealed record ExtendVotingCommand(Guid ElectionId, DateTimeOffset VotingEndAt)
    : ICommand<ElectionResponse>;

public sealed class ExtendVotingCommandValidator : AbstractValidator<ExtendVotingCommand>
{
    public ExtendVotingCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.VotingEndAt).NotEmpty();
    }
}

public sealed class ExtendVotingCommandHandler(
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ExtendVotingCommand, Result<ElectionResponse>>
{
    public async Task<Result<ElectionResponse>> Handle(
        ExtendVotingCommand command,
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

        if (election.Status != ElectionStatus.VotingOpen)
        {
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.VotingNotOpen",
                election.CanBeEdited
                    ? "Voting has not opened yet, so change its dates by editing the election."
                    : $"An election that is {election.Status} cannot be extended."));
        }

        if (!election.ExtendVoting(command.VotingEndAt, actorId, clock.UtcNow))
        {
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.NotLater",
                "Voting can only be kept open longer: choose a time after the current close."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(election.ToResponse(clock.UtcNow, myChoices: null));
    }
}
