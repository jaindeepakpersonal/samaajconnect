using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;
using Sangam.MasterData.Domain.Lookups;

namespace Sangam.MasterData.Application.LookupItems.Commands.CreateLookupItem;

/// <summary>
/// Adds one row to one of the four flat platform-wide lists: Country,
/// ProfessionType, Gender, or MaritalStatus.
/// </summary>
[RequiresPermission(PermissionKeys.MasterDataManage)]
public sealed record CreateLookupItemCommand(
    string Category, string Code, string Label, int SortOrder) : ICommand<LookupItemResponse>;

public sealed class CreateLookupItemCommandValidator : AbstractValidator<CreateLookupItemCommand>
{
    public CreateLookupItemCommandValidator()
    {
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(c => Enum.TryParse<LookupCategory>(c, ignoreCase: true, out _))
            .WithMessage("Category must be one of Country, ProfessionType, Gender, MaritalStatus.");

        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateLookupItemCommandHandler(
    ILookupItemRepository lookupItems,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<CreateLookupItemCommand, Result<LookupItemResponse>>
{
    public async Task<Result<LookupItemResponse>> Handle(
        CreateLookupItemCommand command, CancellationToken cancellationToken)
    {
        var category = Enum.Parse<LookupCategory>(command.Category, ignoreCase: true);

        if (await lookupItems.CodeExistsAsync(category, command.Code, cancellationToken))
        {
            return Result.Failure<LookupItemResponse>(Error.Conflict(
                "LookupItem.DuplicateCode",
                $"A {category} with code '{command.Code}' already exists."));
        }

        var item = LookupItem.Create(category, command.Code, command.Label, command.SortOrder, clock.UtcNow);

        lookupItems.Add(item);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(item.ToResponse());
    }
}
