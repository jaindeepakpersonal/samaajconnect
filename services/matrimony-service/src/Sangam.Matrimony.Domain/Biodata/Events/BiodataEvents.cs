using Sangam.Matrimony.Domain.Common;

namespace Sangam.Matrimony.Domain.Biodata.Events;

/// <summary>
/// Raised on first opt-in and on rejoining after a withdrawal - both are "a
/// biodata profile became searchable", which is the fact worth an audit row.
/// </summary>
public sealed record BiodataProfileCreatedDomainEvent(
    Guid MemberId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "matrimony.biodata.created.v1";
}

public sealed record MatrimonyConsentWithdrawnDomainEvent(
    Guid MemberId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "matrimony.consent.withdrawn.v1";
}
