namespace Sangam.Trust.Application.Expenses;

public sealed record ExpenseResponse(
    Guid Id,
    Guid TrustId,
    string Category,
    string Description,
    long Amount,
    string? Vendor,
    string Status,
    Guid RecordedBy,
    DateTimeOffset RecordedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt);
