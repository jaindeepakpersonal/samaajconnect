using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.Localities.Commands.UpdateLocality;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record UpdateLocalityCommand(Guid Id, string Name) : ICommand<LocalityResponse>;

public sealed class UpdateLocalityCommandValidator : AbstractValidator<UpdateLocalityCommand>
{
    public UpdateLocalityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateLocalityCommandHandler(
    ILocalityRepository localities, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<UpdateLocalityCommand, Result<LocalityResponse>>
{
    public async Task<Result<LocalityResponse>> Handle(
        UpdateLocalityCommand command, CancellationToken cancellationToken)
    {
        var locality = await localities.GetByIdAsync(command.Id, cancellationToken);

        if (locality is null)
        {
            return Result.Failure<LocalityResponse>(Error.NotFound("Locality.NotFound", "No such locality."));
        }

        locality.Update(command.Name, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(locality.ToResponse());
    }
}
