using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.Localities.Commands.DeactivateLocality;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record DeactivateLocalityCommand(Guid Id) : ICommand<LocalityResponse>;

public sealed class DeactivateLocalityCommandValidator : AbstractValidator<DeactivateLocalityCommand>
{
    public DeactivateLocalityCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeactivateLocalityCommandHandler(
    ILocalityRepository localities, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<DeactivateLocalityCommand, Result<LocalityResponse>>
{
    public async Task<Result<LocalityResponse>> Handle(
        DeactivateLocalityCommand command, CancellationToken cancellationToken)
    {
        var locality = await localities.GetByIdAsync(command.Id, cancellationToken);

        if (locality is null)
        {
            return Result.Failure<LocalityResponse>(Error.NotFound("Locality.NotFound", "No such locality."));
        }

        if (!locality.Deactivate(clock.UtcNow))
        {
            return Result.Failure<LocalityResponse>(
                Error.Conflict("Locality.AlreadyInactive", "This locality is already inactive."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(locality.ToResponse());
    }
}
