using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Expenses;

/// <summary>An expense was recorded against a Trust. Starts Recorded.</summary>
/// <remarks>
/// No <c>Category</c>: it is free text an administrator typed, and
/// audit-notification-service stores payloads verbatim in an append-only
/// table. It stays in this service's own table, where it can be corrected.
/// </remarks>
public sealed record ExpenseRecordedDomainEvent(
    Guid ExpenseId,
    Guid TenantId,
    Guid TrustId,
    long Amount,
    Guid RecordedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.expense.recorded.v1";
}

/// <summary>A recorded expense was approved by a second pair of eyes.</summary>
public sealed record ExpenseApprovedDomainEvent(
    Guid ExpenseId,
    Guid TenantId,
    Guid TrustId,
    Guid ApprovedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.expense.approved.v1";
}
