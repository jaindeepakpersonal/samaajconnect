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

/// <summary>A Pathshala has stopped operating and takes no more enrolments.</summary>
public sealed record PathshalaDeactivatedDomainEvent(
    Guid PathshalaId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.deactivated.v1";
}
