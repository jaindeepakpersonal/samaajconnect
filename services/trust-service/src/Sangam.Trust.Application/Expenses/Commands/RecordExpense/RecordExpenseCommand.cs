using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Expenses;

namespace Sangam.Trust.Application.Expenses.Commands.RecordExpense;

/// <summary>Records an expense against a Trust. Starts Recorded, awaiting approval.</summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record RecordExpenseCommand(
    Guid TrustId,
    string Category,
    string Description,
    long Amount,
    string? Vendor) : ICommand<ExpenseResponse>;

public sealed class RecordExpenseCommandValidator : AbstractValidator<RecordExpenseCommand>
{
    public RecordExpenseCommandValidator()
    {
        RuleFor(x => x.TrustId).NotEmpty();
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Vendor).MaximumLength(200);
    }
}

public sealed class RecordExpenseCommandHandler(
    ITrustRepository trusts,
    IExpenseRepository expenses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RecordExpenseCommand, Result<ExpenseResponse>>
{
    public async Task<Result<ExpenseResponse>> Handle(
        RecordExpenseCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<ExpenseResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var trust = await trusts.GetByIdAsync(command.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<ExpenseResponse>(
                Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        var expense = Expense.Record(
            trust.TenantId, trust.Id, command.Category, command.Description, command.Amount,
            command.Vendor, actorId, clock.UtcNow);

        expenses.Add(expense);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(expense.ToResponse());
    }
}
