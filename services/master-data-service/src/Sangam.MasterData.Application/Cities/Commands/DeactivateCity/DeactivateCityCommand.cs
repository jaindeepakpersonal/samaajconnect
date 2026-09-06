using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.Cities.Commands.DeactivateCity;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record DeactivateCityCommand(Guid Id) : ICommand<CityResponse>;

public sealed class DeactivateCityCommandValidator : AbstractValidator<DeactivateCityCommand>
{
    public DeactivateCityCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeactivateCityCommandHandler(
    ICityRepository cities, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<DeactivateCityCommand, Result<CityResponse>>
{
    public async Task<Result<CityResponse>> Handle(
        DeactivateCityCommand command, CancellationToken cancellationToken)
    {
        var city = await cities.GetByIdAsync(command.Id, cancellationToken);

        if (city is null)
        {
            return Result.Failure<CityResponse>(Error.NotFound("City.NotFound", "No such city."));
        }

        if (!city.Deactivate(clock.UtcNow))
        {
            return Result.Failure<CityResponse>(
                Error.Conflict("City.AlreadyInactive", "This city is already inactive."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(city.ToResponse());
    }
}
