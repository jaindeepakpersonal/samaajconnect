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
