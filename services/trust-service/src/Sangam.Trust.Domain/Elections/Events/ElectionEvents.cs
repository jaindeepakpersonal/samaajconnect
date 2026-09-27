using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Elections.Events;

public sealed record TrustElectionStatusChangedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    Guid TrustId,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.election.status-changed.v1";
}

/// <summary>Names the winner - what a published election actually announces.</summary>
public sealed record TrustElectionResultsPublishedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    Guid TrustId,
    Guid RoleId,
    IReadOnlyList<Guid> WinnerMemberIds,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.election.results-published.v1";
}
