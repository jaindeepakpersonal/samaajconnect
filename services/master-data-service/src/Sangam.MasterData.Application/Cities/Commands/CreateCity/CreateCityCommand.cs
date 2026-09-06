using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;
using Sangam.MasterData.Domain.Locations;
using Sangam.MasterData.Domain.Lookups;

namespace Sangam.MasterData.Application.Cities.Commands.CreateCity;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record CreateCityCommand(Guid CountryId, string Name) : ICommand<CityResponse>;

public sealed class CreateCityCommandValidator : AbstractValidator<CreateCityCommand>
{
    public CreateCityCommandValidator()
    {
        RuleFor(x => x.CountryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateCityCommandHandler(
    ICityRepository cities,
    ILookupItemRepository lookupItems,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<CreateCityCommand, Result<CityResponse>>
{
    public async Task<Result<CityResponse>> Handle(CreateCityCommand command, CancellationToken cancellationToken)
    {
        var country = await lookupItems.GetByIdAsync(command.CountryId, cancellationToken);

        if (country is null || country.Category != LookupCategory.Country)
        {
            return Result.Failure<CityResponse>(
                Error.NotFound("City.CountryNotFound", "No such country."));
        }

        var city = City.Create(command.CountryId, command.Name, clock.UtcNow);

        cities.Add(city);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(city.ToResponse());
    }
}
