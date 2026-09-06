using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.Gautras.Commands.DeactivateGautra;

[RequiresPermission(PermissionKeys.GautraManage)]
public sealed record DeactivateGautraCommand(Guid Id) : ICommand<GautraResponse>;

public sealed class DeactivateGautraCommandValidator : AbstractValidator<DeactivateGautraCommand>
{
    public DeactivateGautraCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeactivateGautraCommandHandler(
    IGautraRepository gautras, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<DeactivateGautraCommand, Result<GautraResponse>>
{
    public async Task<Result<GautraResponse>> Handle(
        DeactivateGautraCommand command, CancellationToken cancellationToken)
    {
        var gautra = await gautras.GetByIdAsync(command.Id, cancellationToken);

        if (gautra is null)
        {
            return Result.Failure<GautraResponse>(
                Error.NotFound("Gautra.NotFound", "No such Gautra in this Samaaj."));
        }

        if (!gautra.Deactivate(clock.UtcNow))
        {
            return Result.Failure<GautraResponse>(
                Error.Conflict("Gautra.AlreadyInactive", "This Gautra is already inactive."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(gautra.ToResponse());
    }
}
