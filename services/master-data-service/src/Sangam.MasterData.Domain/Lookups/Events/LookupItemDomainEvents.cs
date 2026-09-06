using Sangam.MasterData.Domain.Common;

namespace Sangam.MasterData.Domain.Lookups.Events;

/// <summary>
/// <see cref="Domain.Common.IDomainEvent.TenantId"/> is <see cref="Guid.Empty"/> on every event
/// here: a lookup item belongs to the platform, not to any one Samaaj.
/// </summary>
public sealed record LookupItemCreatedDomainEvent(
    Guid LookupItemId,
    string Category,
    string Code,
    string Label,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.lookup-item.created.v1";
}

public sealed record LookupItemUpdatedDomainEvent(
    Guid LookupItemId,
    string Category,
    string Code,
    string Label,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.lookup-item.updated.v1";
}

public sealed record LookupItemDeactivatedDomainEvent(
    Guid LookupItemId,
    string Category,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.lookup-item.deactivated.v1";
}
