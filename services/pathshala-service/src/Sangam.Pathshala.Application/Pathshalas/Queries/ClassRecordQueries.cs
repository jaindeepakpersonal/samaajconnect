using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Security;

namespace Sangam.Pathshala.Application.Pathshalas.Queries;

/// <summary>One class's register for one date, as it currently stands.</summary>
/// <remarks>
/// <para>
/// The counterpart to <c>MarkAttendanceCommand</c>, and it was missing. A
/// register may be submitted twice - the second submission amends the first -
/// but until now nothing could read the first one back, so a teacher fixing one
/// child's mark was re-entering the whole class from memory. Every mark not
/// re-sent stays as it was, which means a half-remembered resubmission does not
/// even fail loudly; it just leaves the register subtly wrong.
/// </para>
/// <para>
/// A date with no register comes back empty rather than 404: "nobody has marked
/// this day yet" is a normal state of a register, not a missing resource.
/// </para>
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetClassRegisterQuery(Guid ClassId, DateOnly ClassDate)
    : IQuery<IReadOnlyList<RegisterEntryResponse>>;

public sealed record RegisterEntryResponse(
    Guid EnrolmentId, string Status, DateTimeOffset MarkedAt);

public sealed class GetClassRegisterQueryHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : IRequestHandler<GetClassRegisterQuery, Result<IReadOnlyList<RegisterEntryResponse>>>
{
    public async Task<Result<IReadOnlyList<RegisterEntryResponse>>> Handle(
        GetClassRegisterQuery query, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByClassIdAsync(query.ClassId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<IReadOnlyList<RegisterEntryResponse>>(
                PathshalaAccess.NoSuchClass);
        }

        var pathshalaClass = pathshala!.FindClass(query.ClassId)!;

        // The same gate as the roll, and for the same reason: a register is a
        // record of somebody's children, and holding the attendance permission
        // somewhere is not permission to read this class's.
        if (!PathshalaAccess.CanTeach(
                currentUser, pathshalaClass, PermissionKeys.PathshalaAttendanceWrite))
        {
            return Result.Failure<IReadOnlyList<RegisterEntryResponse>>(
                PathshalaAccess.NoSuchClass);
        }

        var marks = await enrolments.ListRegisterAsync(
            query.ClassId, query.ClassDate, cancellationToken);

        return Result.Success<IReadOnlyList<RegisterEntryResponse>>(
            [.. marks.Select(m => new RegisterEntryResponse(
                m.EnrolmentId, m.Status.ToString(), m.MarkedAt))]);
    }
}

/// <summary>One class's exams, each with the marks recorded in it.</summary>
/// <remarks>
/// <para>
/// Scheduling an exam answered with its id and nothing ever listed them again,
/// so recording a result meant holding that id from the response that created
/// it. An exam scheduled last week could not be marked this week by any route
/// the platform offered.
/// </para>
/// <para>
/// The marks come with the exams rather than from a second endpoint. A teacher
/// entering results needs to know who already has one - re-recording a mark
/// amends it silently, so entering a score against a child already marked is
/// how a correct mark gets replaced by a guess.
/// </para>
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListClassExamsQuery(Guid ClassId)
    : IQuery<IReadOnlyList<ClassExamResponse>>;

public sealed record ClassExamResponse(
    Guid Id,
    Guid ClassId,
    string Title,
    DateOnly ExamDate,
    int MaxScore,
    IReadOnlyList<RecordedResultResponse> Results);

public sealed record RecordedResultResponse(
    Guid EnrolmentId, int Score, string? Grade, DateTimeOffset RecordedAt);

public sealed class ListClassExamsQueryHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : IRequestHandler<ListClassExamsQuery, Result<IReadOnlyList<ClassExamResponse>>>
{
    public async Task<Result<IReadOnlyList<ClassExamResponse>>> Handle(
        ListClassExamsQuery query, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByClassIdAsync(query.ClassId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<IReadOnlyList<ClassExamResponse>>(PathshalaAccess.NoSuchClass);
        }

        var pathshalaClass = pathshala!.FindClass(query.ClassId)!;

        // Gated on writing exams rather than on reading records, because this
        // answers for the whole class at once. A parent entitled to their own
        // child's marks reads them through the progress view, which is scoped
        // to one enrolment.
        if (!PathshalaAccess.CanTeach(
                currentUser, pathshalaClass, PermissionKeys.PathshalaExamsWrite))
        {
            return Result.Failure<IReadOnlyList<ClassExamResponse>>(PathshalaAccess.NoSuchClass);
        }

        var exams = await enrolments.ListExamsForClassAsync(query.ClassId, cancellationToken);
        var results = await enrolments.ListResultsForClassAsync(query.ClassId, cancellationToken);

        var byExam = results
            .GroupBy(r => r.ExamId)
            .ToDictionary(g => g.Key, IReadOnlyList<RecordedResultResponse> (g) =>
                [.. g.Select(r => new RecordedResultResponse(
                    r.EnrolmentId, r.Score, r.Grade, r.RecordedAt))]);

        return Result.Success<IReadOnlyList<ClassExamResponse>>(
        [
            .. exams
                .OrderByDescending(e => e.ExamDate)
                .Select(e => new ClassExamResponse(
                    e.Id,
                    e.ClassId,
                    e.Title,
                    e.ExamDate,
                    e.MaxScore,
                    byExam.GetValueOrDefault(e.Id, [])))
        ]);
    }
}
