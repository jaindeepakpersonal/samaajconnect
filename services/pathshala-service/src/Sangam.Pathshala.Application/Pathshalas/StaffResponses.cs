namespace Sangam.Pathshala.Application.Pathshalas;

// What a Pathshala's administrators and teachers read about it - never a
// parent. Each list is narrowed to the classes the caller may see; see
// PathshalaAccess.StaffScopeOf.

/// <summary>
/// One teacher, with the contact details the Pathshala holds for them.
/// <paramref name="HasLogin"/> is whether they can sign in to mark registers
/// themselves; <paramref name="UserId"/> is that account.
/// </summary>
public sealed record TeacherResponse(
    Guid Id,
    string FullName,
    string? Phone,
    string? Email,
    string? Qualification,
    Guid? UserId,
    bool HasLogin,
    IReadOnlyList<Guid> ClassIds,
    DateTimeOffset AddedAt);

/// <summary>
/// One child with a place or a request, and who to call about them.
/// </summary>
/// <remarks>
/// The child is a profile id - their name is member-family-service's, resolved by
/// the panel. <paramref name="ContactName"/> and <paramref name="ContactPhone"/>
/// are what the parent gave on the request, and null on requests made before
/// the form asked; <paramref name="ParentMemberId"/> is who made it.
/// </remarks>
public sealed record StudentResponse(
    Guid EnrolmentId,
    Guid ChildProfileId,
    string Status,
    Guid? ClassId,
    string? ClassName,
    string? SessionLabel,
    Guid ParentMemberId,
    string? ContactName,
    string? ContactPhone,
    DateTimeOffset RequestedAt,
    DateTimeOffset? EnrolledAt);

/// <summary>
/// One enrolled child's progress: attendance and exam average, computed on read
/// the same way the parent's own progress view is.
/// </summary>
public sealed record ProgressRowResponse(
    Guid EnrolmentId,
    Guid ChildProfileId,
    Guid? ClassId,
    string? ClassName,
    int Present,
    int Absent,
    int Excused,
    int? AttendancePercentage,
    int ExamsSat,
    double? AverageScorePercentage);

/// <summary>One class's register over its life.</summary>
public sealed record ClassAttendanceResponse(
    Guid ClassId,
    string ClassName,
    string SessionLabel,
    int StudentCount,
    int DaysMarked,
    int Present,
    int Absent,
    int Excused,
    int? Percentage);

/// <summary>One exam across the Pathshala, with how far its marking has got.</summary>
public sealed record ExamOverviewResponse(
    Guid ExamId,
    Guid ClassId,
    string ClassName,
    string Title,
    DateOnly ExamDate,
    int MaxScore,
    int StudentCount,
    int ResultsRecorded,
    double? AverageScorePercentage);
