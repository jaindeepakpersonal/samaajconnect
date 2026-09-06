using Sangam.MasterData.Domain.Common;

namespace Sangam.MasterData.Domain.Locations.Events;

public sealed record CityCreatedDomainEvent(
    Guid CityId, Guid CountryId, string Name, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.city.created.v1";
}

public sealed record CityUpdatedDomainEvent(
    Guid CityId, Guid CountryId, string Name, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.city.updated.v1";
}

public sealed record CityDeactivatedDomainEvent(
    Guid CityId, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.city.deactivated.v1";
}
