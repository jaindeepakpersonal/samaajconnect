using Sangam.Pathshala.Domain.Enrolments;
using Sangam.Pathshala.Domain.Pathshalas;

namespace Sangam.Pathshala.Application.Pathshalas;

/// <summary>Domain to wire. No decisions here beyond shape.</summary>
public static class PathshalaMappings
{
    public static PathshalaResponse ToResponse(
        this Domain.Pathshalas.Pathshala pathshala, int studentCount = 0) =>
        new(pathshala.Id,
            pathshala.Name,
            pathshala.Address,
            pathshala.ContactPerson,
            pathshala.Status.ToString(),
            pathshala.CurrentSession?.Label,
            pathshala.CurrentSession?.Id,
            pathshala.Classes.Count,

            // Distinct, because one teacher taking two classes is one teacher.
            pathshala.Classes.SelectMany(c => c.Teachers).Select(t => t.TeacherMemberId)
                .Distinct().Count(),
            pathshala.AcceptsEnrolments);

    public static PathshalaDetailResponse ToDetail(
        this Domain.Pathshalas.Pathshala pathshala,
        IReadOnlyDictionary<Guid, int> studentsByClass) =>
        new(pathshala.Id,
            pathshala.Name,
            pathshala.Address,
            pathshala.ContactPerson,
            pathshala.Status.ToString(),
            pathshala.AcceptsEnrolments,
            [.. pathshala.Sessions
                .OrderByDescending(s => s.StartDate)
                .Select(s => new SessionResponse(
                    s.Id, s.Label, s.StartDate, s.EndDate, s.IsCurrent))],
            [.. pathshala.Classes
                .OrderBy(c => c.Name)
                .Select(c => c.ToResponse(
                    pathshala.FindSession(c.SessionId)?.Label ?? string.Empty,
                    studentsByClass.GetValueOrDefault(c.Id)))]);

    public static ClassResponse ToResponse(
        this PathshalaClass pathshalaClass, string sessionLabel, int studentCount) =>
        new(pathshalaClass.Id,
            pathshalaClass.SessionId,
            sessionLabel,
            pathshalaClass.Name,
            pathshalaClass.RoomLabel,
            [.. pathshalaClass.ToSlots()],
            [.. pathshalaClass.Teachers.Select(t => t.TeacherMemberId)],
            studentCount);

    public static IEnumerable<ScheduleSlotResponse> ToSlots(this PathshalaClass pathshalaClass) =>
        pathshalaClass.Schedule
            .OrderBy(s => s.DayOfWeek)
            .ThenBy(s => s.StartTime)
            .Select(s => new ScheduleSlotResponse(
                s.DayOfWeek.ToString(), s.StartTime, s.EndTime));

    public static EnrolmentResponse ToResponse(
        this StudentEnrolment enrolment,
        string? className = null,
        string? sessionLabel = null) =>
        new(enrolment.Id,
            enrolment.PathshalaId,
            enrolment.ChildProfileId,
            enrolment.ClassId,
            className,
            enrolment.SessionId,
            sessionLabel,
            enrolment.Status.ToString(),
            enrolment.RequestedAt,
            enrolment.EnrolledAt);
}
