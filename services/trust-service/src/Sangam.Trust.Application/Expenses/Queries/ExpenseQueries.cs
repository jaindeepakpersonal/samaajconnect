using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Expenses;

namespace Sangam.Trust.Application.Expenses.Queries;

/// <summary>One Trust's expenses, optionally narrowed to one status, newest first.</summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record ListExpensesQuery(Guid TrustId, ExpenseStatus? Status)
    : IQuery<IReadOnlyList<ExpenseResponse>>;

public sealed class ListExpensesQueryHandler(IExpenseRepository expenses)
    : IRequestHandler<ListExpensesQuery, Result<IReadOnlyList<ExpenseResponse>>>
{
    public async Task<Result<IReadOnlyList<ExpenseResponse>>> Handle(
        ListExpensesQuery query, CancellationToken cancellationToken)
    {
        var found = await expenses.ListByTrustAsync(query.TrustId, query.Status, cancellationToken);

        return Result.Success<IReadOnlyList<ExpenseResponse>>([.. found.Select(e => e.ToResponse())]);
    }
}
