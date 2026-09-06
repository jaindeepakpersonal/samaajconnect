using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;
using Sangam.MasterData.Domain.Locations;

namespace Sangam.MasterData.Application.Localities.Commands.CreateLocality;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record CreateLocalityCommand(Guid CityId, string Name) : ICommand<LocalityResponse>;

public sealed class CreateLocalityCommandValidator : AbstractValidator<CreateLocalityCommand>
{
    public CreateLocalityCommandValidator()
    {
        RuleFor(x => x.CityId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateLocalityCommandHandler(
    ILocalityRepository localities,
    ICityRepository cities,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<CreateLocalityCommand, Result<LocalityResponse>>
{
    public async Task<Result<LocalityResponse>> Handle(
        CreateLocalityCommand command, CancellationToken cancellationToken)
    {
        var city = await cities.GetByIdAsync(command.CityId, cancellationToken);

        if (city is null)
        {
            return Result.Failure<LocalityResponse>(
                Error.NotFound("Locality.CityNotFound", "No such city."));
        }

        var locality = Locality.Create(command.CityId, command.Name, clock.UtcNow);

        localities.Add(locality);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(locality.ToResponse());
    }
}
