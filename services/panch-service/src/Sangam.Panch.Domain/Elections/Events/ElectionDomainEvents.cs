using Sangam.Panch.Domain.Common;

namespace Sangam.Panch.Domain.Elections;

/// <summary>
/// The election moved on. Carries the previous status as well as the new one,
/// per SECURITY-CHECKLIST.md, and no title - the Samaaj has its own copy.
/// </summary>
public sealed record ElectionStatusChangedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "panch.election.status-changed.v1";
}

/// <summary>
/// An administrator corrected the election's title, windows or results
/// visibility before voting opened. Carries no values - the title and
/// description are the Samaaj's own text, and the audit trail is append-only.
/// </summary>
public sealed record ElectionUpdatedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    Guid UpdatedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "panch.election.updated.v1";
}

/// <summary>
/// Voting was kept open longer. Both dates travel, so the audit row shows what
/// members were first told as well as what replaced it.
/// </summary>
public sealed record ElectionVotingExtendedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    DateTimeOffset PreviousVotingEndAt,
    DateTimeOffset VotingEndAt,
    Guid ExtendedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "panch.election.voting-extended.v1";
}

/// <summary>
/// An election was deleted before voting opened. Its row is gone, so this is
/// the only record that it existed and who removed it.
/// </summary>
public sealed record ElectionDeletedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    string Status,
    Guid DeletedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "panch.election.deleted.v1";
}

/// <summary>
/// The Samaaj Panch changed: an election seated a new one, or an administrator
/// removed a member.
/// </summary>
/// <remarks>
/// <b>Carries the whole roster, not a delta.</b> Delivery is at-least-once and
/// consumers (social-issues-service keeps its own copy of who is on the panel)
/// have to be idempotent; a snapshot they replace their rows with is safe to see
/// twice, where "member X added" is not. Ids only - never a name - because
/// audit-notification-service records payloads verbatim into an append-only
/// table.
/// </remarks>
public sealed record PanelChangedDomainEvent(
    Guid TenantId,
    IReadOnlyList<Guid> MemberIds,
    string Reason,
    Guid? ElectionId,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "panch.panel.changed.v1";
}
