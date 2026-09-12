using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.States.Commands.DeactivateState;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record DeactivateStateCommand(Guid Id) : ICommand<StateResponse>;

public sealed class DeactivateStateCommandValidator : AbstractValidator<DeactivateStateCommand>
{
    public DeactivateStateCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeactivateStateCommandHandler(
    IStateRepository states, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<DeactivateStateCommand, Result<StateResponse>>
{
    public async Task<Result<StateResponse>> Handle(
        DeactivateStateCommand command, CancellationToken cancellationToken)
    {
        var state = await states.GetByIdAsync(command.Id, cancellationToken);

        if (state is null)
        {
            return Result.Failure<StateResponse>(Error.NotFound("State.NotFound", "No such state."));
        }

        if (!state.Deactivate(clock.UtcNow))
        {
            return Result.Failure<StateResponse>(
                Error.Conflict("State.AlreadyInactive", "This state is already inactive."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(state.ToResponse());
    }
}
