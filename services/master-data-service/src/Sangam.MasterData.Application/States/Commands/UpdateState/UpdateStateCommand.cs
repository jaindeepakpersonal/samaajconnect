using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.States.Commands.UpdateState;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record UpdateStateCommand(Guid Id, string Name) : ICommand<StateResponse>;

public sealed class UpdateStateCommandValidator : AbstractValidator<UpdateStateCommand>
{
    public UpdateStateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateStateCommandHandler(
    IStateRepository states, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<UpdateStateCommand, Result<StateResponse>>
{
    public async Task<Result<StateResponse>> Handle(UpdateStateCommand command, CancellationToken cancellationToken)
    {
        var state = await states.GetByIdAsync(command.Id, cancellationToken);

        if (state is null)
        {
            return Result.Failure<StateResponse>(Error.NotFound("State.NotFound", "No such state."));
        }

        state.Update(command.Name, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(state.ToResponse());
    }
}
