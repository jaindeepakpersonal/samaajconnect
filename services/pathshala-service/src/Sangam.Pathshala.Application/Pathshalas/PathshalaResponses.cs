namespace Sangam.Pathshala.Application.Pathshalas;

/// <summary>
/// A Pathshala as the directory shows it.
/// </summary>
/// <remarks>
/// Counts rather than rosters. The wireframe's card says "3 teachers • 8
/// classes • 126 students", which is three numbers the database can produce
/// without sending anybody a list of children.
/// </remarks>
public sealed record PathshalaResponse(
    Guid Id,
    string Name,
    string? Address,
    string? ContactPerson,
    string Status,
    string? CurrentSessionLabel,
    Guid? CurrentSessionId,
    int ClassCount,
    int TeacherCount,
    bool AcceptsEnrolments);

public sealed record PathshalaDetailResponse(
    Guid Id,
    string Name,
    string? Address,
    string? ContactPerson,
    string Status,
    bool AcceptsEnrolments,
    IReadOnlyList<SessionResponse> Sessions,
    IReadOnlyList<ClassResponse> Classes);

public sealed record SessionResponse(
    Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, bool IsCurrent);

/// <summary>
/// A class, its slots and its teachers.
/// </summary>
/// <remarks>
/// Teachers are member ids, not names. A name here would be a copy of
/// member-family-service's data kept in step by nothing; the portal resolves
/// ids it already has to resolve for every other screen.
/// </remarks>
public sealed record ClassResponse(
    Guid Id,
    Guid SessionId,
    string SessionLabel,
    string Name,
    string? RoomLabel,
    IReadOnlyList<ScheduleSlotResponse> Schedule,
    IReadOnlyList<Guid> TeacherMemberIds,
    int StudentCount);

public sealed record ScheduleSlotResponse(string DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

/// <summary>
/// One child's place, as a parent or the Pathshala sees it.
/// </summary>
/// <remarks>
/// <paramref name="ClassId"/> and <paramref name="ClassName"/> are null while
/// the request is still waiting to be placed, which is the state the parent
/// most needs to be able to tell apart from being enrolled.
/// </remarks>
public sealed record EnrolmentResponse(
    Guid Id,
    Guid PathshalaId,
    Guid ChildProfileId,
    Guid? ClassId,
    string? ClassName,
    Guid? SessionId,
    string? SessionLabel,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? EnrolledAt);

/// <summary>The wireframe's "My Class" card.</summary>
public sealed record MyClassResponse(
    Guid EnrolmentId,
    Guid PathshalaId,
    string PathshalaName,
    Guid ClassId,
    string ClassName,
    string? RoomLabel,
    string SessionLabel,
    IReadOnlyList<ScheduleSlotResponse> Schedule,
    IReadOnlyList<Guid> TeacherMemberIds,
    int ClassmateCount);

/// <summary>The wireframe's "My Attendance": the three tiles, and the detail.</summary>
public sealed record MyAttendanceResponse(
    Guid EnrolmentId,
    int? Percentage,
    int Present,
    int Absent,
    int Excused,
    IReadOnlyList<AttendanceDayResponse> Days);

public sealed record AttendanceDayResponse(DateOnly ClassDate, string Status);

/// <summary>
/// The wireframe's "My Exams" table: upcoming and completed in one list.
/// </summary>
/// <remarks>
/// <paramref name="Score"/> is null both for an exam not yet sat and for one
/// sat but not yet marked; <paramref name="Status"/> is what tells those apart.
/// </remarks>
public sealed record MyExamResponse(
    Guid ExamId,
    string Title,
    DateOnly ExamDate,
    int MaxScore,
    string Status,
    int? Score,
    string? Grade);

/// <summary>
/// The wireframe's "My Progress".
/// </summary>
/// <remarks>
/// Computed on read rather than stored. DATA-MODEL.md has a
/// <c>ProgressRecord</c> holding <c>AttendancePct</c> and
/// <c>AverageScore</c>, but both are counts over tables this service already
/// owns, and a stored copy is a copy that drifts - a corrected mark or an
/// amended register would leave the progress screen quietly wrong until
/// something recomputed it. Only <c>ParticipationNotes</c>, which nothing can
/// derive, would have justified the table, and no screen asks for it yet.
/// </remarks>
public sealed record MyProgressResponse(
    Guid EnrolmentId,
    string? SessionLabel,
    int? AttendancePercentage,
    int Present,
    int Absent,
    int Excused,
    int ExamsSat,
    double? AverageScorePercentage);
