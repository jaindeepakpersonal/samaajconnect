using Sangam.Panch.Domain.Common;

namespace Sangam.Panch.Domain.Elections;

/// <summary>
/// The election moved on. Carries the previous status as well as the new one,
/// per SECURITY-CHECKLIST.md, and no title - the Samaaj has its own copy.
/// </summary>
/// <remarks>
/// <b>Names the administrator who moved it.</b> Opening voting, closing it and
/// publishing the result are the decisions an election turns on - closing is
/// the one way voting can end early - so "who did this?" has to have an
/// answer. Until 2026-10-25 the event carried none, and the audit row for every
/// status change had a blank actor.
/// </remarks>
public sealed record ElectionStatusChangedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    string PreviousStatus,
    string Status,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "panch.election.status-changed.v1";
}

/// <summary>
/// An administrator approved a nomination onto the ballot, or turned it down.
/// </summary>
/// <remarks>
/// <b>A turned-down nomination leaves no row behind</b>, so this is the only
/// record that somebody was put forward and kept off the ballot, and by whom -
/// the question a member asks when their name never appeared. Ids only: the
/// candidate's member id and the administrator's, never a name.
/// </remarks>
public sealed record CandidateDecidedDomainEvent(
    Guid ElectionId,
    Guid TenantId,
    Guid CandidateId,
    Guid MemberId,
    string Decision,
    Guid DecidedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "panch.candidate.decided.v1";
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
