using Sangam.CelebrityVoting.Domain.Common;

namespace Sangam.CelebrityVoting.Domain.Campaigns;

/// <summary>
/// The campaign moved on. Carries the previous status as well as the new one,
/// per SECURITY-CHECKLIST.md, and no title - the Samaaj has its own copy.
/// </summary>
public sealed record CampaignStatusChangedDomainEvent(
    Guid CampaignId,
    Guid TenantId,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "celebrity-voting.campaign.status-changed.v1";
}

/// <summary>
/// Voting is over. Separate from the status change because it is the moment a
/// notification channel would care about most - everyone who voted is waiting
/// to hear - and filtering every status move for it is work a consumer should
/// not have to do.
/// </summary>
public sealed record CampaignClosedDomainEvent(
    Guid CampaignId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "celebrity-voting.campaign.closed.v1";
}

/// <summary>
/// The result, in order.
/// </summary>
/// <remarks>
/// Candidate ids rather than member ids or names. A member's standing in a
/// popularity vote is about them, and audit-notification-service records
/// payloads verbatim into an append-only table; an id that means nothing
/// without this service's own tables is the right amount to put there.
/// </remarks>
public sealed record ResultsPublishedDomainEvent(
    Guid CampaignId,
    Guid TenantId,
    IReadOnlyList<Guid> RankedCandidateIds,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "celebrity-voting.results.published.v1";
}
