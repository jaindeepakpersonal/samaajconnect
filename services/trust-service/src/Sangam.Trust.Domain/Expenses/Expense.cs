using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Expenses;

/// <summary>One expense a Trust has recorded, and whether it has been approved.</summary>
public sealed class Expense : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid TrustId { get; private set; }

    public string Category { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public long Amount { get; private set; }

    public string? Vendor { get; private set; }

    public ExpenseStatus Status { get; private set; }

    public Guid RecordedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    private Expense() { }   // EF Core

    public static Expense Record(
        Guid tenantId,
        Guid trustId,
        string category,
        string description,
        long amount,
        string? vendor,
        Guid recordedBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TrustId = trustId,
            Category = category.Trim(),
            Description = description.Trim(),
            Amount = amount,
            Vendor = string.IsNullOrWhiteSpace(vendor) ? null : vendor.Trim(),
            Status = ExpenseStatus.Recorded,
            RecordedBy = recordedBy,
            RecordedAt = now,
        };

        expense.Raise(new ExpenseRecordedDomainEvent(
            expense.Id, tenantId, trustId, amount, recordedBy, now));

        return expense;
    }

    /// <summary>
    /// A second pair of eyes confirming the expense. Idempotent: approving an
    /// already-approved expense is a no-op reported as success, the same
    /// "repeat action is not a second fact" convention this service's own
    /// <c>TrustMember</c> already uses.
    /// </summary>
    public bool Approve(Guid approvedBy, DateTimeOffset now)
    {
        if (Status == ExpenseStatus.Approved)
        {
            return false;
        }

        Status = ExpenseStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = now;

        Raise(new ExpenseApprovedDomainEvent(Id, TenantId, TrustId, approvedBy, now));

        return true;
    }
}

public enum ExpenseStatus
{
    Recorded = 1,
    Approved = 2,
}
