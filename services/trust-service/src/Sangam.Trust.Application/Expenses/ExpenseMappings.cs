using Sangam.Trust.Domain.Expenses;

namespace Sangam.Trust.Application.Expenses;

internal static class ExpenseMappings
{
    public static ExpenseResponse ToResponse(this Expense expense) => new(
        expense.Id,
        expense.TrustId,
        expense.Category,
        expense.Description,
        expense.Amount,
        expense.Vendor,
        expense.Status.ToString(),
        expense.RecordedBy,
        expense.RecordedAt,
        expense.ApprovedBy,
        expense.ApprovedAt);
}
