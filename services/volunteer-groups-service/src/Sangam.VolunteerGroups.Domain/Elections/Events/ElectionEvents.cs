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

/// <summary>
/// The election's details were corrected - most often its nomination window
/// pushed out. Ids only: the position title and the windows are the group's
/// own copy, and audit-notification-service records payloads verbatim into an
/// append-only table.
/// </summary>
public sealed record GroupElectionUpdatedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    Guid GroupId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.election.updated.v1";
}

public sealed record GroupElectionClosedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    Guid GroupId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.election.closed.v1";
}

/// <summary>Names the winners - what a published election actually announces.</summary>
/// <remarks>
/// A role may have several seats, so <c>WinnerMemberIds</c> carries all of
/// them. <c>WinnerMemberId</c> stays, as the first, so a v1 consumer reading
/// it still gets what it always did.
/// </remarks>
public sealed record GroupElectionResultsPublishedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    Guid GroupId,
    Guid WinnerMemberId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<Guid>? WinnerMemberIds = null) : IDomainEvent
{
    public string Topic => "volunteer-groups.election.results-published.v1";
}
