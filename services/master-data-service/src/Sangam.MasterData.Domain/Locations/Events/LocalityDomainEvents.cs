using Sangam.MasterData.Domain.Common;

namespace Sangam.MasterData.Domain.Locations.Events;

public sealed record LocalityCreatedDomainEvent(
    Guid LocalityId, Guid CityId, string Name, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.locality.created.v1";
}

public sealed record LocalityUpdatedDomainEvent(
    Guid LocalityId, Guid CityId, string Name, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.locality.updated.v1";
}

public sealed record LocalityDeactivatedDomainEvent(
    Guid LocalityId, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.locality.deactivated.v1";
}
