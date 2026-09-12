using Sangam.MasterData.Domain.Common;

namespace Sangam.MasterData.Domain.Locations.Events;

public sealed record StateCreatedDomainEvent(
    Guid StateId, Guid CountryId, string Name, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.state.created.v1";
}

public sealed record StateUpdatedDomainEvent(
    Guid StateId, Guid CountryId, string Name, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.state.updated.v1";
}

public sealed record StateDeactivatedDomainEvent(
    Guid StateId, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid TenantId => Guid.Empty;

    public string Topic => "master-data.state.deactivated.v1";
}
