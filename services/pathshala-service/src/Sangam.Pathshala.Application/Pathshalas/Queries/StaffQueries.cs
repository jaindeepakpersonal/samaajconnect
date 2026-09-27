using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Pathshalas.Commands;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Enrolments;

namespace Sangam.Pathshala.Application.Pathshalas.Queries;

// The admin panel's Pathshala tabs: Teachers, Students, Progress report,
// Attendance, Exams. Each is gated on Members.Read - which every administrator
// and every teacher account holds - and then decided against the data:
// PathshalaAccess.StaffScopeOf says whether the caller runs this Pathshala (every
// class) or teaches at it (their classes), and anybody else is answered "not
// found". A parent never reads these; their own child's records have their own
// queries.

internal static class StaffReads
{
    /// <summary>The Pathshala and the caller's view of it, or the one refusal every staff read gives.</summary>
    public static async Task<Result<(Domain.Pathshalas.Pathshala Pathshala, StaffScope Scope)>> LoadAsync(
        IPathshalaRepository pathshalas,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IAreaScope areaScope,
        Guid pathshalaId,
        CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(pathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId)
            || PathshalaAccess.StaffScopeOf(currentUser, areaScope, pathshala!) is not { } scope)
        {
            return Result.Failure<(Domain.Pathshalas.Pathshala, StaffScope)>(PathshalaAccess.NoSuchPathshala);
        }

        return Result.Success((pathshala!, scope));
    }

    public static double? Average(IEnumerable<double> percentages)
    {
        var list = percentages.ToList();

        return list.Count == 0 ? null : Math.Round(list.Average(), 1);
    }
}

// ---- Teachers ----------------------------------------------------------------

/// <summary>
/// A Pathshala's teachers with their contact details. Its administrators and its
/// teachers read it - colleagues need to reach each other - and nobody else.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead, PermissionKeys.PathshalaTeach)]
public sealed record ListTeachersQuery(Guid PathshalaId) : IQuery<IReadOnlyList<TeacherResponse>>;

public sealed class ListTeachersQueryHandler(
    IPathshalaRepository pathshalas,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope)
    : IRequestHandler<ListTeachersQuery, Result<IReadOnlyList<TeacherResponse>>>
{
    public async Task<Result<IReadOnlyList<TeacherResponse>>> Handle(
        ListTeachersQuery query, CancellationToken cancellationToken)
    {
        var loaded = await StaffReads.LoadAsync(
            pathshalas, currentUser, tenantContext, areaScope, query.PathshalaId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<TeacherResponse>>(loaded.Error);
        }

        var (pathshala, _) = loaded.Value;

        return Result.Success<IReadOnlyList<TeacherResponse>>(
            [.. pathshala.Teachers.OrderBy(t => t.FullName).Select(t => t.ToResponse(pathshala))]);
    }
}

// ---- Students ----------------------------------------------------------------

/// <summary>
/// Every child with a place, and every request waiting for one, with the
/// contact the parent gave. A teacher sees the children placed in their own
/// classes; the waiting requests are the administrators'.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead, PermissionKeys.PathshalaTeach)]
public sealed record ListStudentsQuery(Guid PathshalaId) : IQuery<IReadOnlyList<StudentResponse>>;

public sealed class ListStudentsQueryHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope)
    : IRequestHandler<ListStudentsQuery, Result<IReadOnlyList<StudentResponse>>>
{
    public async Task<Result<IReadOnlyList<StudentResponse>>> Handle(
        ListStudentsQuery query, CancellationToken cancellationToken)
    {
        var loaded = await StaffReads.LoadAsync(
            pathshalas, currentUser, tenantContext, areaScope, query.PathshalaId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<StudentResponse>>(loaded.Error);
        }

        var (pathshala, scope) = loaded.Value;
        var all = await enrolments.ListForPathshalaAsync(pathshala.Id, status: null, cancellationToken);

        var visible = all.Where(e =>
            (e.Status == EnrolmentStatus.Active && e.ClassId is { } classId && scope.ClassIds.Contains(classId))
            || (e.Status == EnrolmentStatus.Requested && scope.IsAdministrator));

        return Result.Success<IReadOnlyList<StudentResponse>>(
            [.. visible
                .OrderBy(e => e.Status)
                .ThenBy(e => e.ClassId is { } c ? pathshala.FindClass(c)?.Name : null)
                .ThenBy(e => e.RequestedAt)
                .Select(e =>
                {
                    var pathshalaClass = e.ClassId is { } c ? pathshala.FindClass(c) : null;

                    return new StudentResponse(
                        e.Id,
                        e.ChildProfileId,
                        e.Status.ToString(),
                        e.ClassId,
                        pathshalaClass?.Name,
                        e.SessionId is { } s ? pathshala.FindSession(s)?.Label : null,
                        e.RequestedByMemberId,
                        e.ContactName,
                        e.ContactPhone,
                        e.RequestedAt,
                        e.EnrolledAt);
                })]);
    }
}

// ---- Progress report ---------------------------------------------------------

/// <summary>
/// Every placed child's attendance and exam average - the parent's own progress
/// view, for a whole Pathshala, in two GROUP BYs rather than a query per child.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead, PermissionKeys.PathshalaTeach)]
public sealed record GetProgressReportQuery(Guid PathshalaId) : IQuery<IReadOnlyList<ProgressRowResponse>>;

public sealed class GetProgressReportQueryHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope)
    : IRequestHandler<GetProgressReportQuery, Result<IReadOnlyList<ProgressRowResponse>>>
{
    public async Task<Result<IReadOnlyList<ProgressRowResponse>>> Handle(
        GetProgressReportQuery query, CancellationToken cancellationToken)
    {
        var loaded = await StaffReads.LoadAsync(
            pathshalas, currentUser, tenantContext, areaScope, query.PathshalaId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ProgressRowResponse>>(loaded.Error);
        }

        var (pathshala, scope) = loaded.Value;

        var placed = (await enrolments.ListForPathshalaAsync(pathshala.Id, EnrolmentStatus.Active, cancellationToken))
            .Where(e => e.ClassId is { } c && scope.ClassIds.Contains(c))
            .ToList();

        var attendance = await enrolments.TallyAttendanceByEnrolmentAsync(pathshala.Id, cancellationToken);
        var exams = await enrolments.ListExamsForClassesAsync(scope.ClassIds.ToList(), cancellationToken);
        var maxScores = exams.ToDictionary(e => e.Id, e => e.MaxScore);
        var results = await enrolments.ListResultsForExamsAsync(maxScores.Keys.ToList(), cancellationToken);
        var resultsByEnrolment = results.ToLookup(r => r.EnrolmentId);

        return Result.Success<IReadOnlyList<ProgressRowResponse>>(
            [.. placed
                .Select(e =>
                {
                    var tally = attendance.GetValueOrDefault(e.Id) ?? new AttendanceTally(0, 0, 0);

                    // A percentage of each exam's own maximum, never raw marks
                    // - an exam out of 20 and one out of 100 are not comparable.
                    var mine = resultsByEnrolment[e.Id]
                        .Where(r => maxScores.GetValueOrDefault(r.ExamId) > 0)
                        .Select(r => 100.0 * r.Score / maxScores[r.ExamId])
                        .ToList();

                    return new ProgressRowResponse(
                        e.Id,
                        e.ChildProfileId,
                        e.ClassId,
                        e.ClassId is { } c ? pathshala.FindClass(c)?.Name : null,
                        tally.Present,
                        tally.Absent,
                        tally.Excused,
                        tally.Percentage,
                        mine.Count,
                        StaffReads.Average(mine));
                })
                .OrderBy(r => r.ClassName)]);
    }
}

// ---- Attendance --------------------------------------------------------------

/// <summary>Per class: how many days the register was marked, and how it stands.</summary>
[RequiresPermission(PermissionKeys.MembersRead, PermissionKeys.PathshalaTeach)]
public sealed record GetAttendanceOverviewQuery(Guid PathshalaId) : IQuery<IReadOnlyList<ClassAttendanceResponse>>;

public sealed class GetAttendanceOverviewQueryHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope)
    : IRequestHandler<GetAttendanceOverviewQuery, Result<IReadOnlyList<ClassAttendanceResponse>>>
{
    public async Task<Result<IReadOnlyList<ClassAttendanceResponse>>> Handle(
        GetAttendanceOverviewQuery query, CancellationToken cancellationToken)
    {
        var loaded = await StaffReads.LoadAsync(
            pathshalas, currentUser, tenantContext, areaScope, query.PathshalaId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ClassAttendanceResponse>>(loaded.Error);
        }

        var (pathshala, scope) = loaded.Value;
        var byClass = await enrolments.TallyAttendanceByClassAsync(scope.ClassIds.ToList(), cancellationToken);
        var placed = await enrolments.ListForPathshalaAsync(pathshala.Id, EnrolmentStatus.Active, cancellationToken);
        var studentsByClass = placed.Where(e => e.ClassId is not null).GroupBy(e => e.ClassId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return Result.Success<IReadOnlyList<ClassAttendanceResponse>>(
            [.. pathshala.Classes
                .Where(c => scope.ClassIds.Contains(c.Id))
                .OrderBy(c => c.Name)
                .Select(c =>
                {
                    var marked = byClass.GetValueOrDefault(c.Id) ?? new ClassAttendance(0, new AttendanceTally(0, 0, 0));

                    return new ClassAttendanceResponse(
                        c.Id,
                        c.Name,
                        pathshala.FindSession(c.SessionId)?.Label ?? string.Empty,
                        studentsByClass.GetValueOrDefault(c.Id),
                        marked.DaysMarked,
                        marked.Tally.Present,
                        marked.Tally.Absent,
                        marked.Tally.Excused,
                        marked.Tally.Percentage);
                })]);
    }
}

// ---- Exams -------------------------------------------------------------------

/// <summary>Every exam across the classes the caller may see, newest first, with how far marking has got.</summary>
[RequiresPermission(PermissionKeys.MembersRead, PermissionKeys.PathshalaTeach)]
public sealed record ListPathshalaExamsQuery(Guid PathshalaId) : IQuery<IReadOnlyList<ExamOverviewResponse>>;

public sealed class ListPathshalaExamsQueryHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope)
    : IRequestHandler<ListPathshalaExamsQuery, Result<IReadOnlyList<ExamOverviewResponse>>>
{
    public async Task<Result<IReadOnlyList<ExamOverviewResponse>>> Handle(
        ListPathshalaExamsQuery query, CancellationToken cancellationToken)
    {
        var loaded = await StaffReads.LoadAsync(
            pathshalas, currentUser, tenantContext, areaScope, query.PathshalaId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ExamOverviewResponse>>(loaded.Error);
        }

        var (pathshala, scope) = loaded.Value;
        var exams = await enrolments.ListExamsForClassesAsync(scope.ClassIds.ToList(), cancellationToken);
        var results = (await enrolments.ListResultsForExamsAsync([.. exams.Select(e => e.Id)], cancellationToken))
            .ToLookup(r => r.ExamId);
        var placed = await enrolments.ListForPathshalaAsync(pathshala.Id, EnrolmentStatus.Active, cancellationToken);
        var studentsByClass = placed.Where(e => e.ClassId is not null).GroupBy(e => e.ClassId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return Result.Success<IReadOnlyList<ExamOverviewResponse>>(
            [.. exams.Select(exam =>
            {
                var marks = results[exam.Id].ToList();

                return new ExamOverviewResponse(
                    exam.Id,
                    exam.ClassId,
                    pathshala.FindClass(exam.ClassId)?.Name ?? string.Empty,
                    exam.Title,
                    exam.ExamDate,
                    exam.MaxScore,
                    studentsByClass.GetValueOrDefault(exam.ClassId),
                    marks.Count,
                    exam.MaxScore > 0 ? StaffReads.Average(marks.Select(m => 100.0 * m.Score / exam.MaxScore)) : null);
            })]);
    }
}
