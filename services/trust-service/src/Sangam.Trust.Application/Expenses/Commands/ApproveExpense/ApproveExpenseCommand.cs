using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;

namespace Sangam.Trust.Application.Expenses.Commands.ApproveExpense;

/// <summary>
/// A second pair of eyes confirming a recorded expense. Approving an
/// already-approved expense is a no-op reported as success.
/// </summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record ApproveExpenseCommand(Guid ExpenseId) : ICommand<ExpenseResponse>;

public sealed class ApproveExpenseCommandValidator : AbstractValidator<ApproveExpenseCommand>
{
    public ApproveExpenseCommandValidator() => RuleFor(x => x.ExpenseId).NotEmpty();
}

public sealed class ApproveExpenseCommandHandler(
    IExpenseRepository expenses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ApproveExpenseCommand, Result<ExpenseResponse>>
{
    public async Task<Result<ExpenseResponse>> Handle(
        ApproveExpenseCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<ExpenseResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var expense = await expenses.GetByIdAsync(command.ExpenseId, cancellationToken);

        if (expense is null)
        {
            return Result.Failure<ExpenseResponse>(
                Error.NotFound("Expense.NotFound", "No such expense in this Samaaj."));
        }

        if (expense.Approve(actorId, clock.UtcNow))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(expense.ToResponse());
    }
}
