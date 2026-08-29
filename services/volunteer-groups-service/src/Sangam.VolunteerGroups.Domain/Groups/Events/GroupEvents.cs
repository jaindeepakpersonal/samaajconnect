using Sangam.VolunteerGroups.Domain.Common;

namespace Sangam.VolunteerGroups.Domain.Groups;

/// <summary>
/// A group was created. Ids only - the name and description are the Samaaj's
/// own copy, and audit-notification-service records payloads verbatim into an
/// append-only table.
/// </summary>
public sealed record GroupCreatedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid PresidentMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.group.created.v1";
}

/// <summary>
/// Somebody asked to join. Carries no application note: that is what a member
/// wrote about themselves, and it belongs to the president who has to read it,
/// not to an append-only log.
/// </summary>
public sealed record GroupApplicationSubmittedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid ApplicationId,
    Guid MemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.application.submitted.v1";
}

/// <summary>
/// The president decided. Names who decided as well as who was decided about:
/// "who let them in?" is the first question asked when a group turns out to
/// contain somebody it should not.
/// </summary>
public sealed record GroupApplicationDecidedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid ApplicationId,
    Guid MemberId,
    Guid DecidedBy,
    bool Accepted,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.application.decided.v1";
}

/// <summary>A member was given, or relieved of, a position inside the group.</summary>
public sealed record GroupRolePositionAssignedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid MemberId,
    string? RolePosition,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.role-position.assigned.v1";
}

/// <summary>
/// The group changed hands. Carries both presidents, so the audit log answers
/// "who was running this group in March?" without replaying every change.
/// </summary>
public sealed record GroupPresidentChangedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid PreviousPresidentMemberId,
    Guid PresidentMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.president.changed.v1";
}

/// <summary>Carries the previous status as well as the new one, per SECURITY-CHECKLIST.md.</summary>
public sealed record GroupStatusChangedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.group.status-changed.v1";
}
