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
    Guid? PresidentMemberId,
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

/// <summary>A member was moved into a role inside the group.</summary>
/// <remarks>
/// <c>RoleId</c> was added with defined roles; <c>RolePosition</c> stays, as
/// the role's title, so a v1 consumer reading it still gets what it always
/// did.
/// </remarks>
public sealed record GroupRolePositionAssignedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid MemberId,
    string? RolePosition,
    DateTimeOffset OccurredAt,
    Guid? RoleId = null) : IDomainEvent
{
    public string Topic => "volunteer-groups.role-position.assigned.v1";
}

/// <summary>A group defined a new volunteer role. Ids only; the title is in this service's own table.</summary>
public sealed record GroupRoleDefinedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid RoleId,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.role.defined.v1";
}

/// <summary>A group renamed a volunteer role or changed how many seats it has.</summary>
public sealed record GroupRoleUpdatedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid RoleId,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.role.updated.v1";
}

/// <summary>A group removed a volunteer role nobody held.</summary>
public sealed record GroupRoleRemovedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid RoleId,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.role.removed.v1";
}

/// <summary>
/// The president removed a member. Names who removed them for the same reason
/// <see cref="GroupApplicationDecidedDomainEvent"/> names who decided: "who let
/// them in, and who put them out?" is one question asked about the same group.
/// </summary>
public sealed record GroupMemberRemovedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid MemberId,
    Guid RemovedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.member.removed.v1";
}

/// <summary>
/// The group changed hands. Carries both presidents, so the audit log answers
/// "who was running this group in March?" without replaying every change.
/// </summary>
public sealed record GroupPresidentChangedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    Guid? PreviousPresidentMemberId,
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

/// <summary>
/// The group's name, description or focus area was corrected. Ids only, same
/// reasoning as <see cref="GroupCreatedDomainEvent"/> - the new text is the
/// Samaaj's own copy, not audit-notification-service's to hold.
/// </summary>
public sealed record GroupUpdatedDomainEvent(
    Guid GroupId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "volunteer-groups.group.updated.v1";
}
