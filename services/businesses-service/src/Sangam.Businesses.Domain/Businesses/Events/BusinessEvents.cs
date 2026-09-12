using Sangam.Businesses.Domain.Common;

namespace Sangam.Businesses.Domain.Businesses.Events;

public sealed record BusinessCreatedDomainEvent(
    Guid BusinessId,
    Guid TenantId,
    Guid OwnerMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "businesses.business.created.v1";
}

/// <summary>No event for Product CRUD or for the fields `Update` touches - routine self-service catalog edits with no other consumer and no distinct actor, the same reasoning `LookupItem` field edits get no event beyond Created/Updated/Deactivated.</summary>
public sealed record BusinessStatusChangedDomainEvent(
    Guid BusinessId,
    Guid TenantId,
    Guid OwnerMemberId,
    bool IsActive,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "businesses.business.status-changed.v1";
}
