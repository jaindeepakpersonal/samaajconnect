using Sangam.Pathshala.Domain.Common;

namespace Sangam.Pathshala.Domain.Enrolments.Events;

/// <summary>
/// A parent has asked for a place. Published because somebody at the Pathshala
/// has to act on it and there is no other trigger - the placement queue is a
/// screen nobody would think to open unprompted.
/// </summary>
public sealed record EnrolmentRequestedDomainEvent(
    Guid EnrolmentId,
    Guid TenantId,
    Guid PathshalaId,
    Guid ChildProfileId,
    Guid RequestedByMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.enrolment.requested.v1";
}

/// <summary>
/// The child has a class. This is `StudentEnrolled` from SERVICES.md, named for
/// the moment it actually happens rather than for the request.
/// </summary>
public sealed record StudentEnrolledDomainEvent(
    Guid EnrolmentId,
    Guid TenantId,
    Guid PathshalaId,
    Guid ClassId,
    Guid SessionId,
    Guid ChildProfileId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.student.enrolled.v1";
}

/// <summary>
/// A mark has been recorded.
/// </summary>
/// <remarks>
/// Carries the score, which is the one payload here that is genuinely about a
/// child. It is on the event because a notification to the parent is the
/// obvious consumer and a score nobody can read is not worth sending; the
/// child is named only by an enrolment id, which means nothing outside this
/// service's tables.
/// </remarks>
public sealed record ExamResultRecordedDomainEvent(
    Guid ExamId,
    Guid TenantId,
    Guid ClassId,
    Guid EnrolmentId,
    int Score,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.exam-result.recorded.v1";
}
