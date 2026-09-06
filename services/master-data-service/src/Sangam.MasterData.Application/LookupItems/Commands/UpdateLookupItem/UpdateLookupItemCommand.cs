using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.LookupItems.Commands.UpdateLookupItem;

/// <summary>
/// Renames a lookup item or moves it within its category's display order.
/// The code and category are fixed once created — other services may already
/// hold this row's id, and changing what it means underneath them is worse
/// than adding a new row.
/// </summary>
[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record UpdateLookupItemCommand(Guid Id, string Label, int SortOrder)
    : ICommand<LookupItemResponse>;

public sealed class UpdateLookupItemCommandValidator : AbstractValidator<UpdateLookupItemCommand>
{
    public UpdateLookupItemCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateLookupItemCommandHandler(
    ILookupItemRepository lookupItems,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateLookupItemCommand, Result<LookupItemResponse>>
{
    public async Task<Result<LookupItemResponse>> Handle(
        UpdateLookupItemCommand command, CancellationToken cancellationToken)
    {
        var item = await lookupItems.GetByIdAsync(command.Id, cancellationToken);

        if (item is null)
        {
            return Result.Failure<LookupItemResponse>(
                Error.NotFound("LookupItem.NotFound", "No such lookup item."));
        }

        item.Update(command.Label, command.SortOrder, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(item.ToResponse());
    }
}
