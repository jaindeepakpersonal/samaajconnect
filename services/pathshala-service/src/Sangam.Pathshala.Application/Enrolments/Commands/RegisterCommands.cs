using FluentValidation;
using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Enrolments;

namespace Sangam.Pathshala.Application.Enrolments.Commands;

// ---- Marking a register ------------------------------------------------------

/// <summary>One student's mark, within a register submission.</summary>
public sealed record AttendanceMark(Guid EnrolmentId, string Status);

/// <summary>
/// Marks a whole class's register for one date.
/// </summary>
/// <remarks>
/// <b>The register is one submission, not twenty-five.</b> A teacher fills the
/// list in and sends it once, so it is one command, one transaction and one
/// answer. Twenty-five separate calls from a phone on a Pathshala's wifi is how
/// half a register ends up recorded.
///
/// <b>Re-marking amends rather than duplicates.</b> Correcting Present to
/// Excused after a parent explains is the ordinary case, and the unique index on
/// <c>(EnrolmentId, ClassDate)</c> is what makes it possible to treat it that
/// way: a second submission finds the existing rows instead of writing beside
/// them.
/// </remarks>
[RequiresPermission(PermissionKeys.PathshalaAttendanceWrite)]
public sealed record MarkAttendanceCommand(
    Guid ClassId, DateOnly ClassDate, IReadOnlyList<AttendanceMark> Marks)
    : ICommand<MarkAttendanceResponse>;

/// <summary>
/// <paramref name="Recorded"/> counts rows written for the first time and
/// <paramref name="Amended"/> those changed, so a teacher who submits twice can
/// see that the second submission corrected rather than added.
/// </summary>
public sealed record MarkAttendanceResponse(
    Guid ClassId, DateOnly ClassDate, int Recorded, int Amended, int Ignored);

public sealed class MarkAttendanceCommandValidator : AbstractValidator<MarkAttendanceCommand>
{
    public MarkAttendanceCommandValidator()
    {
        RuleFor(x => x.ClassId).NotEmpty();
        RuleFor(x => x.Marks).NotEmpty().WithMessage("A register needs at least one mark.");

        RuleForEach(x => x.Marks).ChildRules(mark =>
        {
            mark.RuleFor(m => m.EnrolmentId).NotEmpty();

            mark.RuleFor(m => m.Status)
                .NotEmpty()
                .Must(s => Enum.TryParse<AttendanceStatus>(s, ignoreCase: true, out _))
                .WithMessage(
                    $"Status must be one of: {string.Join(", ", Enum.GetNames<AttendanceStatus>())}.");
        });

        // One row per student per submission. Two marks for one child in one
        // payload is a client bug, and letting it through would make the answer
        // depend on which one the database happened to write last.
        RuleFor(x => x.Marks)
            .Must(marks => marks.Select(m => m.EnrolmentId).Distinct().Count() == marks.Count)
            .WithMessage("The register lists the same student twice.");
    }
}

public sealed class MarkAttendanceCommandHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,

    // No IUnitOfWork: the whole register is written on the repository's own
    // connection, deliberately outside this request's transaction.
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<MarkAttendanceCommand, Result<MarkAttendanceResponse>>
{
    public async Task<Result<MarkAttendanceResponse>> Handle(
        MarkAttendanceCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } markedBy)
        {
            return Result.Failure<MarkAttendanceResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var pathshala = await pathshalas.GetByClassIdAsync(command.ClassId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<MarkAttendanceResponse>(PathshalaAccess.NoSuchClass);
        }

        var pathshalaClass = pathshala!.FindClass(command.ClassId)!;

        // Holding Pathshala.Attendance.Write is not enough: it says teacher, not
        // teacher *of this class*.
        if (!PathshalaAccess.CanTeach(
                currentUser, pathshalaClass, PermissionKeys.PathshalaAttendanceWrite))
        {
            return Result.Failure<MarkAttendanceResponse>(PathshalaAccess.NoSuchClass);
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        if (command.ClassDate > today)
        {
            return Result.Failure<MarkAttendanceResponse>(Error.Conflict(
                "Attendance.Future", "A register cannot be marked before the class has met."));
        }

        if (!pathshalaClass.MeetsOn(command.ClassDate))
        {
            return Result.Failure<MarkAttendanceResponse>(Error.Conflict(
                "Attendance.NotAClassDay", "This class does not meet on that day."));
        }

        // The roll decides who can be marked. A withdrawn student is silently
        // ignored rather than refused: a teacher working from a printed list
        // should not have a whole register rejected because one child left.
        var roll = (await enrolments.ListForClassAsync(command.ClassId, cancellationToken))
            .ToDictionary(e => e.Id);

        var writable = new List<RegisterMark>(command.Marks.Count);
        var ignored = 0;

        foreach (var mark in command.Marks)
        {
            if (!roll.TryGetValue(mark.EnrolmentId, out var enrolment) || !enrolment.IsOnRoll)
            {
                ignored++;
                continue;
            }

            writable.Add(new RegisterMark(
                enrolment.Id,
                enrolment.TenantId,
                Enum.Parse<AttendanceStatus>(mark.Status, ignoreCase: true)));
        }

        // The whole register - read, amend and insert - goes to the repository,
        // which does it on one connection of its own. Amending here and
        // inserting there would put the request's transaction and that
        // connection in contention for the same rows; see
        // IEnrolmentRepository.SaveRegisterAsync.
        var outcome = writable.Count == 0
            ? new RegisterOutcome(0, 0)
            : await enrolments.SaveRegisterAsync(
                command.ClassId,
                command.ClassDate,
                writable,
                markedBy,
                clock.UtcNow,
                cancellationToken);

        return Result.Success(new MarkAttendanceResponse(
            command.ClassId, command.ClassDate, outcome.Recorded, outcome.Amended, ignored));
    }
}

// ---- Exams -------------------------------------------------------------------

/// <summary>Sets an exam for a class.</summary>
[RequiresPermission(PermissionKeys.PathshalaExamsWrite)]
public sealed record ScheduleExamCommand(
    Guid ClassId, string Title, DateOnly ExamDate, int MaxScore) : ICommand<ExamResponse>;

public sealed record ExamResponse(
    Guid Id, Guid ClassId, string Title, DateOnly ExamDate, int MaxScore, int ResultsRecorded);

public sealed class ScheduleExamCommandValidator : AbstractValidator<ScheduleExamCommand>
{
    public ScheduleExamCommandValidator()
    {
        RuleFor(x => x.ClassId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);

        RuleFor(x => x.MaxScore)
            .InclusiveBetween(1, 1000)
            .WithMessage("An exam is marked out of between 1 and 1000.");
    }
}

public sealed class ScheduleExamCommandHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : IRequestHandler<ScheduleExamCommand, Result<ExamResponse>>
{
    public async Task<Result<ExamResponse>> Handle(
        ScheduleExamCommand command, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByClassIdAsync(command.ClassId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<ExamResponse>(PathshalaAccess.NoSuchClass);
        }

        var pathshalaClass = pathshala!.FindClass(command.ClassId)!;

        if (!PathshalaAccess.CanTeach(
                currentUser, pathshalaClass, PermissionKeys.PathshalaExamsWrite))
        {
            return Result.Failure<ExamResponse>(PathshalaAccess.NoSuchClass);
        }

        var exam = Exam.Schedule(
            pathshala.TenantId,
            command.ClassId,
            command.Title,
            command.ExamDate,
            command.MaxScore);

        enrolments.AddExam(exam);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ExamResponse(
            exam.Id, exam.ClassId, exam.Title, exam.ExamDate, exam.MaxScore, ResultsRecorded: 0));
    }
}

/// <summary>
/// Records one student's mark, or corrects one already recorded.
/// </summary>
/// <remarks>
/// Held to one row per student per exam by a unique index on
/// <c>(ExamId, EnrolmentId)</c>. Marking twice amends, for the same reason
/// re-marking a register does: a corrected mark is normal, and a second row
/// would make the average depend on which was read.
/// </remarks>
[RequiresPermission(PermissionKeys.PathshalaExamsWrite)]
public sealed record RecordExamResultCommand(
    Guid ExamId, Guid EnrolmentId, int Score, string? Grade) : ICommand<ExamResultResponse>;

public sealed record ExamResultResponse(
    Guid ExamId, Guid EnrolmentId, int Score, int MaxScore, string? Grade, bool Amended);

public sealed class RecordExamResultCommandValidator : AbstractValidator<RecordExamResultCommand>
{
    public RecordExamResultCommandValidator()
    {
        RuleFor(x => x.ExamId).NotEmpty();
        RuleFor(x => x.EnrolmentId).NotEmpty();
        RuleFor(x => x.Score).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Grade).MaximumLength(10);
    }
}

public sealed class RecordExamResultCommandHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RecordExamResultCommand, Result<ExamResultResponse>>
{
    public async Task<Result<ExamResultResponse>> Handle(
        RecordExamResultCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } recordedBy)
        {
            return Result.Failure<ExamResultResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var exam = await enrolments.GetExamAsync(command.ExamId, cancellationToken);

        if (!exam.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<ExamResultResponse>(PathshalaAccess.NoSuchExam);
        }

        var pathshala = await pathshalas.GetByClassIdAsync(exam!.ClassId, cancellationToken);
        var pathshalaClass = pathshala?.FindClass(exam.ClassId);

        if (pathshalaClass is null
            || !PathshalaAccess.CanTeach(
                currentUser, pathshalaClass, PermissionKeys.PathshalaExamsWrite))
        {
            return Result.Failure<ExamResultResponse>(PathshalaAccess.NoSuchExam);
        }

        if (!exam.Accepts(command.Score))
        {
            return Result.Failure<ExamResultResponse>(Error.Conflict(
                "ExamResult.OutOfRange",
                $"This exam is marked out of {exam.MaxScore}."));
        }

        var enrolment = await enrolments.GetByIdAsync(command.EnrolmentId, cancellationToken);

        if (enrolment is null || enrolment.ClassId != exam.ClassId)
        {
            // Not in this class. Answered as "no such enrolment" rather than as
            // a mismatch, which would confirm the enrolment exists elsewhere.
            return Result.Failure<ExamResultResponse>(PathshalaAccess.NoSuchEnrolment);
        }

        var existing = await enrolments.FindResultAsync(
            exam.Id, enrolment.Id, cancellationToken);

        if (existing is not null)
        {
            existing.Amend(command.Score, command.Grade, recordedBy, clock.UtcNow);
        }
        else
        {
            enrolments.AddResult(new ExamResult(
                exam.TenantId,
                exam.Id,
                enrolment.Id,
                command.Score,
                command.Grade,
                recordedBy,
                clock.UtcNow));

            exam.AnnounceResult(enrolment.Id, command.Score, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ExamResultResponse(
            exam.Id,
            enrolment.Id,
            command.Score,
            exam.MaxScore,
            command.Grade,
            Amended: existing is not null));
    }
}
