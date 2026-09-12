using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;
using Sangam.MasterData.Domain.Locations;
using Sangam.MasterData.Domain.Lookups;

namespace Sangam.MasterData.Application.States.Commands.CreateState;

[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record CreateStateCommand(Guid CountryId, string Name) : ICommand<StateResponse>;

public sealed class CreateStateCommandValidator : AbstractValidator<CreateStateCommand>
{
    public CreateStateCommandValidator()
    {
        RuleFor(x => x.CountryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateStateCommandHandler(
    IStateRepository states,
    ILookupItemRepository lookupItems,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<CreateStateCommand, Result<StateResponse>>
{
    public async Task<Result<StateResponse>> Handle(CreateStateCommand command, CancellationToken cancellationToken)
    {
        var country = await lookupItems.GetByIdAsync(command.CountryId, cancellationToken);

        if (country is null || country.Category != LookupCategory.Country)
        {
            return Result.Failure<StateResponse>(
                Error.NotFound("State.CountryNotFound", "No such country."));
        }

        var state = State.Create(command.CountryId, command.Name, clock.UtcNow);

        states.Add(state);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(state.ToResponse());
    }
}
