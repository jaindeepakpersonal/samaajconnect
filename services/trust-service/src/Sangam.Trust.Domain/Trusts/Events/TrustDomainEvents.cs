using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Trusts;

/// <summary>A Trust was set up for a locality that had none.</summary>
public sealed record TrustCreatedDomainEvent(
    Guid TrustId,
    Guid TenantId,
    Guid LocalityId,
    string Name,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.trust.created.v1";
}

/// <summary>
/// A Trust's status changed. Carries the previous status as well as the new
/// one, per SECURITY-CHECKLIST.md.
/// </summary>
public sealed record TrustStatusChangedDomainEvent(
    Guid TrustId,
    Guid TenantId,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.trust.status-changed.v1";
}
