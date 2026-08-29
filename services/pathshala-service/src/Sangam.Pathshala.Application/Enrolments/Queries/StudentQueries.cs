using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Pathshalas;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Enrolments;
using Sangam.Pathshala.Domain.Pathshalas;

namespace Sangam.Pathshala.Application.Enrolments.Queries;

/// <summary>
/// The four student-facing views, each keyed by an enrolment.
/// </summary>
/// <remarks>
/// All four are gated on <c>Members.Read</c> and then decided against the data
/// by <see cref="PathshalaAccess.CanReadRecordsOf"/>. See
/// <see cref="PermissionKeys"/> for why they are not gated on the
/// <c>PathshalaStudent</c> role, which nothing grants.
///
/// A caller who may not read an enrolment is told it does not exist. These are
/// records about somebody's child, and a 403 would let anybody with a member
/// account confirm which enrolment ids are real.
/// </remarks>
internal static class StudentView
{
    /// <summary>
    /// Loads an enrolment with its Pathshala, refusing anyone who may not see
    /// it. The shared preamble of all four views.
    /// </summary>
    public static async Task<Result<(StudentEnrolment Enrolment, Domain.Pathshalas.Pathshala Pathshala)>>
        ResolveAsync(
            Guid enrolmentId,
            IEnrolmentRepository enrolments,
            IPathshalaRepository pathshalas,
            ICurrentUser currentUser,
            ITenantContext tenantContext,
            CancellationToken cancellationToken)
    {
        var enrolment = await enrolments.GetByIdAsync(enrolmentId, cancellationToken);

        if (!enrolment.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<(StudentEnrolment, Domain.Pathshalas.Pathshala)>(
                PathshalaAccess.NoSuchEnrolment);
        }

        var pathshala = await pathshalas.GetByIdAsync(enrolment!.PathshalaId, cancellationToken);

        if (pathshala is null || !PathshalaAccess.CanReadRecordsOf(currentUser, enrolment, pathshala))
        {
            return Result.Failure<(StudentEnrolment, Domain.Pathshalas.Pathshala)>(
                PathshalaAccess.NoSuchEnrolment);
        }

        return Result.Success((enrolment, pathshala));
    }

    public static PathshalaClass? ClassOf(
        StudentEnrolment enrolment, Domain.Pathshalas.Pathshala pathshala) =>
        enrolment.ClassId is { } classId ? pathshala.FindClass(classId) : null;
}

// ---- My Class ----------------------------------------------------------------

/// <summary>The wireframe's "My Class" card.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetMyClassQuery(Guid EnrolmentId) : IQuery<MyClassResponse>;

public sealed class GetMyClassQueryHandler(
    IEnrolmentRepository enrolments,
    IPathshalaRepository pathshalas,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : IRequestHandler<GetMyClassQuery, Result<MyClassResponse>>
{
    public async Task<Result<MyClassResponse>> Handle(
        GetMyClassQuery query, CancellationToken cancellationToken)
    {
        var resolved = await StudentView.ResolveAsync(
            query.EnrolmentId, enrolments, pathshalas, currentUser, tenantContext,
            cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<MyClassResponse>(resolved.Error);
        }

        var (enrolment, pathshala) = resolved.Value;
        var pathshalaClass = StudentView.ClassOf(enrolment, pathshala);

        if (pathshalaClass is null)
        {
            // Requested but not yet placed. Distinct from "no such enrolment",
            // because the parent needs to be able to tell waiting from refused.
            return Result.Failure<MyClassResponse>(Error.Conflict(
                "Enrolment.NotPlaced", "This child has not been placed in a class yet."));
        }

        var roll = await enrolments.ListForClassAsync(pathshalaClass.Id, cancellationToken);

        return Result.Success(new MyClassResponse(
            enrolment.Id,
            pathshala.Id,
            pathshala.Name,
            pathshalaClass.Id,
            pathshalaClass.Name,
            pathshalaClass.RoomLabel,
            pathshala.FindSession(pathshalaClass.SessionId)?.Label ?? string.Empty,
            [.. pathshalaClass.ToSlots()],
            [.. pathshalaClass.Teachers.Select(t => t.TeacherMemberId)],

            // The wireframe's "Students: 24" - a count, not a list of other
            // people's children.
            roll.Count));
    }
}

// ---- My Attendance -----------------------------------------------------------

/// <summary>The wireframe's "My Attendance": three tiles and the detail behind them.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetMyAttendanceQuery(Guid EnrolmentId) : IQuery<MyAttendanceResponse>;

public sealed class GetMyAttendanceQueryHandler(
    IEnrolmentRepository enrolments,
    IPathshalaRepository pathshalas,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : IRequestHandler<GetMyAttendanceQuery, Result<MyAttendanceResponse>>
{
    public async Task<Result<MyAttendanceResponse>> Handle(
        GetMyAttendanceQuery query, CancellationToken cancellationToken)
    {
        var resolved = await StudentView.ResolveAsync(
            query.EnrolmentId, enrolments, pathshalas, currentUser, tenantContext,
            cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<MyAttendanceResponse>(resolved.Error);
        }

        var (enrolment, _) = resolved.Value;

        var tally = await enrolments.TallyAttendanceAsync(enrolment.Id, cancellationToken);

        var days = await enrolments.ListAttendanceForEnrolmentAsync(
            enrolment.Id, cancellationToken);

        return Result.Success(new MyAttendanceResponse(
            enrolment.Id,
            tally.Percentage,
            tally.Present,
            tally.Absent,
            tally.Excused,
            [.. days
                .OrderByDescending(d => d.ClassDate)
                .Select(d => new AttendanceDayResponse(d.ClassDate, d.Status.ToString()))]));
    }
}

// ---- My Exams ----------------------------------------------------------------

/// <summary>The wireframe's "My Exams": upcoming and completed in one table.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetMyExamsQuery(Guid EnrolmentId) : IQuery<IReadOnlyList<MyExamResponse>>;

public sealed class GetMyExamsQueryHandler(
    IEnrolmentRepository enrolments,
    IPathshalaRepository pathshalas,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<GetMyExamsQuery, Result<IReadOnlyList<MyExamResponse>>>
{
    public async Task<Result<IReadOnlyList<MyExamResponse>>> Handle(
        GetMyExamsQuery query, CancellationToken cancellationToken)
    {
        var resolved = await StudentView.ResolveAsync(
            query.EnrolmentId, enrolments, pathshalas, currentUser, tenantContext,
            cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<IReadOnlyList<MyExamResponse>>(resolved.Error);
        }

        var (enrolment, _) = resolved.Value;

        if (enrolment.ClassId is not { } classId)
        {
            return Result.Success<IReadOnlyList<MyExamResponse>>([]);
        }

        var exams = await enrolments.ListExamsForClassAsync(classId, cancellationToken);

        var results = (await enrolments.ListResultsForEnrolmentAsync(
                enrolment.Id, cancellationToken))
            .ToDictionary(r => r.ExamId);

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        return Result.Success<IReadOnlyList<MyExamResponse>>(
            [.. exams.OrderByDescending(e => e.ExamDate).Select(exam =>
            {
                results.TryGetValue(exam.Id, out var result);

                // Three states, not two. The wireframe shows Upcoming and
                // Completed; an exam sat last week and not yet marked is
                // neither, and showing it as Completed with a blank result
                // reads as a zero.
                var status = result is not null
                    ? "Completed"
                    : exam.HasBeenSat(today) ? "AwaitingResult" : "Upcoming";

                return new MyExamResponse(
                    exam.Id, exam.Title, exam.ExamDate, exam.MaxScore,
                    status, result?.Score, result?.Grade);
            })]);
    }
}

// ---- My Progress -------------------------------------------------------------

/// <summary>
/// The wireframe's "My Progress", computed rather than stored.
/// </summary>
/// <remarks>
/// See <see cref="MyProgressResponse"/> for why there is no
/// <c>ProgressRecord</c> table behind this.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetMyProgressQuery(Guid EnrolmentId) : IQuery<MyProgressResponse>;

public sealed class GetMyProgressQueryHandler(
    IEnrolmentRepository enrolments,
    IPathshalaRepository pathshalas,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : IRequestHandler<GetMyProgressQuery, Result<MyProgressResponse>>
{
    public async Task<Result<MyProgressResponse>> Handle(
        GetMyProgressQuery query, CancellationToken cancellationToken)
    {
        var resolved = await StudentView.ResolveAsync(
            query.EnrolmentId, enrolments, pathshalas, currentUser, tenantContext,
            cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<MyProgressResponse>(resolved.Error);
        }

        var (enrolment, pathshala) = resolved.Value;

        var tally = await enrolments.TallyAttendanceAsync(enrolment.Id, cancellationToken);

        var results = await enrolments.ListResultsForEnrolmentAsync(
            enrolment.Id, cancellationToken);

        double? average = null;

        if (results.Count > 0 && enrolment.ClassId is { } classId)
        {
            var exams = (await enrolments.ListExamsForClassAsync(classId, cancellationToken))
                .ToDictionary(e => e.Id);

            // Averaged as percentages, not as raw scores. An exam out of 20 and
            // one out of 100 are not comparable marks, and averaging the raw
            // numbers would let the longer paper decide the result.
            var percentages = results
                .Where(r => exams.ContainsKey(r.ExamId))
                .Select(r => 100.0 * r.Score / exams[r.ExamId].MaxScore)
                .ToList();

            if (percentages.Count > 0)
            {
                average = Math.Round(percentages.Average(), 1);
            }
        }

        return Result.Success(new MyProgressResponse(
            enrolment.Id,
            enrolment.SessionId is { } sessionId
                ? pathshala.FindSession(sessionId)?.Label
                : null,
            tally.Percentage,
            tally.Present,
            tally.Absent,
            tally.Excused,
            results.Count,
            average));
    }
}
