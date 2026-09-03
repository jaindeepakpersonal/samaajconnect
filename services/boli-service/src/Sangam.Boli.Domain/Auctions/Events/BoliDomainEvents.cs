using Sangam.Boli.Domain.Common;

namespace Sangam.Boli.Domain.Auctions.Events;

/// <summary>
/// An occasion is over. No title in the payload — the Samaaj has its own copy,
/// and audit-notification-service records payloads verbatim into an append-only
/// table, so anything put here is kept forever whether or not it needed to be.
/// </summary>
public sealed record OccasionClosedDomainEvent(
    Guid OccasionId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "boli.occasion.closed.v1";
}

/// <summary>
/// Bidding on one Boli has ended.
/// </summary>
/// <remarks>
/// Separate from the result because the two are minutes or hours apart and mean
/// different things to a listener: this one says stop bidding, and the other
/// says who won. A notification channel would send both, to different people.
/// </remarks>
public sealed record BoliClosedDomainEvent(
    Guid BoliId,
    Guid TenantId,
    Guid OccasionId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "boli.closed.v1";
}

/// <summary>
/// The result has been announced to the Samaaj.
/// </summary>
/// <remarks>
/// The winning member's id is here because unlike a celebrity-voting ranking —
/// where a member's standing in a popularity vote is about them, and the event
/// carries only candidate ids — winning a Boli is a public act with a payment
/// attached. Who won and for how much is what the Samaaj announces in the room,
/// and a downstream receipt or ledger cannot do its job without both.
/// </remarks>
public sealed record BoliResultPublishedDomainEvent(
    Guid BoliId,
    Guid TenantId,
    Guid OccasionId,
    Guid WinningMemberId,
    long Amount,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "boli.result.published.v1";
}

/// <summary>
/// A bid in the closing seconds pushed the close out.
/// </summary>
/// <remarks>
/// Carries both times, because "it moved" is not useful to a listener without
/// knowing where from and where to — a notification saying bidding is still open
/// has to be able to say until when.
///
/// Raised only when the window actually moved, not on every bid. Most bids are
/// nowhere near the close, and an event per bid on the platform's busiest write
/// path would be an outbox row per bid for something almost never true.
/// </remarks>
public sealed record BoliExtendedDomainEvent(
    Guid BoliId,
    Guid TenantId,
    Guid OccasionId,
    DateTimeOffset PreviousEndAt,
    DateTimeOffset EndAt,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "boli.extended.v1";
}
