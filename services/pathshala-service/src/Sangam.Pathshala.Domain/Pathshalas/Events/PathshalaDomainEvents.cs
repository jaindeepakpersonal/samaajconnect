using Sangam.Pathshala.Domain.Common;

namespace Sangam.Pathshala.Domain.Pathshalas.Events;

/// <summary>
/// A new Pathshala exists. Ids only - the name is in this service's own table,
/// and audit-notification-service records payloads verbatim into an
/// append-only table.
/// </summary>
public sealed record PathshalaCreatedDomainEvent(
    Guid PathshalaId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.created.v1";
}

/// <summary>
/// A new academic session is current, which is what decides where new
/// enrolments land.
/// </summary>
/// <remarks>
/// Carries the label because it is the one piece of text that is about the
/// Pathshala's calendar rather than about a person, and a notification saying
/// "enrolment is open" is unusable without it.
/// </remarks>
public sealed record AcademicSessionOpenedDomainEvent(
    Guid PathshalaId,
    Guid TenantId,
    Guid SessionId,
    string Label,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.session.opened.v1";
}

/// <summary>
/// A Pathshala's name, address or contact person was corrected. Ids only, like
/// its creation: the new values are in this service's own table.
/// </summary>
public sealed record PathshalaUpdatedDomainEvent(
    Guid PathshalaId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.updated.v1";
}

/// <summary>
/// A teacher was added to a Pathshala. Ids only: their name, phone and email
/// are personal data held in this service's own table, where they can be
/// corrected - not in the append-only audit log.
/// </summary>
public sealed record TeacherAddedDomainEvent(
    Guid PathshalaId, Guid TenantId, Guid TeacherId, Guid ChangedBy, DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.teacher.added.v1";
}

/// <summary>A teacher's details or login were corrected. Ids only.</summary>
public sealed record TeacherUpdatedDomainEvent(
    Guid PathshalaId, Guid TenantId, Guid TeacherId, Guid ChangedBy, DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.teacher.updated.v1";
}

/// <summary>A teacher was removed, and with them their class assignments. Ids only.</summary>
public sealed record TeacherRemovedDomainEvent(
    Guid PathshalaId, Guid TenantId, Guid TeacherId, Guid ChangedBy, DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.teacher.removed.v1";
}

/// <summary>A Pathshala has stopped operating and takes no more enrolments.</summary>
public sealed record PathshalaDeactivatedDomainEvent(
    Guid PathshalaId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.deactivated.v1";
}
