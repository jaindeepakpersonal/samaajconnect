using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.LookupItems.Commands.DeactivateLookupItem;

/// <summary>
/// Retires a lookup item. Never a hard delete — see <c>LookupItem.Deactivate</c>
/// for why: other services hold this row's id as a plain foreign key with no
/// referential integrity across the service boundary to enforce it.
/// </summary>
[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record DeactivateLookupItemCommand(Guid Id) : ICommand<LookupItemResponse>;

public sealed class DeactivateLookupItemCommandValidator : AbstractValidator<DeactivateLookupItemCommand>
{
    public DeactivateLookupItemCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeactivateLookupItemCommandHandler(
    ILookupItemRepository lookupItems,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<DeactivateLookupItemCommand, Result<LookupItemResponse>>
{
    public async Task<Result<LookupItemResponse>> Handle(
        DeactivateLookupItemCommand command, CancellationToken cancellationToken)
    {
        var item = await lookupItems.GetByIdAsync(command.Id, cancellationToken);

        if (item is null)
        {
            return Result.Failure<LookupItemResponse>(
                Error.NotFound("LookupItem.NotFound", "No such lookup item."));
        }

        if (!item.Deactivate(clock.UtcNow))
        {
            return Result.Failure<LookupItemResponse>(Error.Conflict(
                "LookupItem.AlreadyInactive", "This lookup item is already inactive."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(item.ToResponse());
    }
}
