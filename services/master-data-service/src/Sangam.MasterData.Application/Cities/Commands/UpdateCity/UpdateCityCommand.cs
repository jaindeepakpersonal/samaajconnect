using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.Cities.Commands.UpdateCity;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record UpdateCityCommand(Guid Id, string Name) : ICommand<CityResponse>;

public sealed class UpdateCityCommandValidator : AbstractValidator<UpdateCityCommand>
{
    public UpdateCityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateCityCommandHandler(
    ICityRepository cities, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<UpdateCityCommand, Result<CityResponse>>
{
    public async Task<Result<CityResponse>> Handle(UpdateCityCommand command, CancellationToken cancellationToken)
    {
        var city = await cities.GetByIdAsync(command.Id, cancellationToken);

        if (city is null)
        {
            return Result.Failure<CityResponse>(Error.NotFound("City.NotFound", "No such city."));
        }

        city.Update(command.Name, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(city.ToResponse());
    }
}
