using Sangam.MasterData.Domain.Common;

namespace Sangam.MasterData.Domain.Gautras.Events;

/// <summary>
/// Unlike the platform-wide lookup events in this service, <see cref="TenantId"/>
/// here is a real Samaaj id - a Gautra list belongs to exactly one Samaaj.
/// </summary>
public sealed record GautraCreatedDomainEvent(
    Guid GautraId, Guid TenantId, string Name, DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "master-data.gautra.created.v1";
}

public sealed record GautraUpdatedDomainEvent(
    Guid GautraId, Guid TenantId, string Name, DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "master-data.gautra.updated.v1";
}

public sealed record GautraDeactivatedDomainEvent(
    Guid GautraId, Guid TenantId, DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "master-data.gautra.deactivated.v1";
}
