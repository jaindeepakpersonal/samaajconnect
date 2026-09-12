using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.Cities.Commands.UpdateCity;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record UpdateCityCommand(Guid Id, string? StateId, string Name) : ICommand<CityResponse>;

public sealed class UpdateCityCommandValidator : AbstractValidator<UpdateCityCommand>
{
    public UpdateCityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.StateId).Must(BeNullOrAGuid).WithMessage("State is not valid.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }

    private static bool BeNullOrAGuid(string? value) => value is null || Guid.TryParse(value, out _);
}

public sealed class UpdateCityCommandHandler(
    ICityRepository cities, IStateRepository states, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<UpdateCityCommand, Result<CityResponse>>
{
    public async Task<Result<CityResponse>> Handle(UpdateCityCommand command, CancellationToken cancellationToken)
    {
        var city = await cities.GetByIdAsync(command.Id, cancellationToken);

        if (city is null)
        {
            return Result.Failure<CityResponse>(Error.NotFound("City.NotFound", "No such city."));
        }

        // Guaranteed null-or-parseable by UpdateCityCommandValidator.
        Guid? stateId = command.StateId is null ? null : Guid.Parse(command.StateId);

        if (stateId is { } id)
        {
            var state = await states.GetByIdAsync(id, cancellationToken);

            if (state is null || state.CountryId != city.CountryId)
            {
                return Result.Failure<CityResponse>(
                    Error.NotFound("City.StateNotFound", "No such state in this country."));
            }
        }

        city.Update(stateId, command.Name, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(city.ToResponse());
    }
}
