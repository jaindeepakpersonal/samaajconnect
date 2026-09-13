using Sangam.VolunteerGroups.Domain.Common;

namespace Sangam.VolunteerGroups.Domain.Elections.Events;

public sealed record GroupElectionStatusChangedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    Guid GroupId,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.election.status-changed.v1";
}

public sealed record GroupElectionClosedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    Guid GroupId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.election.closed.v1";
}

/// <summary>Names the winner - what a published election actually announces.</summary>
public sealed record GroupElectionResultsPublishedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    Guid GroupId,
    Guid WinnerMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.election.results-published.v1";
}
