using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;
using Sangam.MasterData.Domain.Locations;
using Sangam.MasterData.Domain.Lookups;

namespace Sangam.MasterData.Application.Cities.Commands.CreateCity;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record CreateCityCommand(Guid CountryId, string? StateId, string Name) : ICommand<CityResponse>;

public sealed class CreateCityCommandValidator : AbstractValidator<CreateCityCommand>
{
    public CreateCityCommandValidator()
    {
        RuleFor(x => x.CountryId).NotEmpty();
        RuleFor(x => x.StateId).Must(BeNullOrAGuid).WithMessage("State is not valid.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }

    private static bool BeNullOrAGuid(string? value) => value is null || Guid.TryParse(value, out _);
}

public sealed class CreateCityCommandHandler(
    ICityRepository cities,
    IStateRepository states,
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

        // Guaranteed null-or-parseable by CreateCityCommandValidator.
        Guid? stateId = command.StateId is null ? null : Guid.Parse(command.StateId);

        if (stateId is { } id)
        {
            var state = await states.GetByIdAsync(id, cancellationToken);

            if (state is null || state.CountryId != command.CountryId)
            {
                return Result.Failure<CityResponse>(
                    Error.NotFound("City.StateNotFound", "No such state in this country."));
            }
        }

        var city = City.Create(command.CountryId, stateId, command.Name, clock.UtcNow);

        cities.Add(city);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(city.ToResponse());
    }
}
