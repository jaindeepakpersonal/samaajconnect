using Sangam.Pathshala.Domain.Common;
using Sangam.Pathshala.Domain.Enrolments.Events;

namespace Sangam.Pathshala.Domain.Enrolments;

/// <summary>
/// One student, one class date, one mark.
/// </summary>
/// <remarks>
/// <b>Not part of the enrolment aggregate, and the unique index on
/// <c>(EnrolmentId, ClassDate)</c> is what keeps it honest.</b>
///
/// A teacher marking a register submits twenty-five rows at once, often from a
/// phone on a bad connection, and often twice because the first attempt looked
/// like it failed. Two rows for one child on one day is not a cosmetic problem:
/// every number this service reports - attendance percentage, present count,
/// the progress view - is a count over this table, so a duplicate silently
/// inflates a child's record and there is nothing on the screen to notice it
/// by.
///
/// A check-then-insert in the handler does not prevent it. Two submissions of
/// the same register arriving together both read no existing row and both
/// write one. Only the database can refuse the second, and it does. Re-marking
/// is therefore an <i>update</i> of the existing row rather than an error: a
/// teacher correcting Present to Excused is the ordinary case, not an
/// exceptional one.
/// </remarks>
public sealed class AttendanceEntry : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EnrolmentId { get; private set; }
    public Guid ClassId { get; private set; }
    public DateOnly ClassDate { get; private set; }
    public AttendanceStatus Status { get; private set; }
    public Guid MarkedByMemberId { get; private set; }
    public DateTimeOffset MarkedAt { get; private set; }

    private AttendanceEntry() { }   // EF Core

    public AttendanceEntry(
        Guid tenantId,
        Guid enrolmentId,
        Guid classId,
        DateOnly classDate,
        AttendanceStatus status,
        Guid markedByMemberId,
        DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        EnrolmentId = enrolmentId;
        ClassId = classId;
        ClassDate = classDate;
        Status = status;
        MarkedByMemberId = markedByMemberId;
        MarkedAt = now;
    }

    /// <summary>Corrects a mark already taken, recording who changed it.</summary>
    public void Amend(AttendanceStatus status, Guid markedByMemberId, DateTimeOffset now)
    {
        Status = status;
        MarkedByMemberId = markedByMemberId;
        MarkedAt = now;
    }
}

public enum AttendanceStatus
{
    Present = 1,
    Absent = 2,

    /// <summary>Absent, but not counted against them.</summary>
    Excused = 3,
}

/// <summary>An examination set for a class.</summary>
public sealed class Exam : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ClassId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public DateOnly ExamDate { get; private set; }
    public int MaxScore { get; private set; }

    private Exam() { }   // EF Core

    public static Exam Schedule(
        Guid tenantId, Guid classId, string title, DateOnly examDate, int maxScore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxScore, 1);

        return new Exam
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClassId = classId,
            Title = title.Trim(),
            ExamDate = examDate,
            MaxScore = maxScore,
        };
    }

    /// <summary>Whether the exam has been sat, as of <paramref name="today"/>.</summary>
    public bool HasBeenSat(DateOnly today) => ExamDate <= today;

    /// <summary>Whether <paramref name="score"/> is a possible mark for this exam.</summary>
    public bool Accepts(int score) => score >= 0 && score <= MaxScore;

    /// <summary>
    /// Announces a mark. Raised here rather than on <see cref="ExamResult"/>,
    /// which is written directly and is not an aggregate root of its own.
    /// </summary>
    public void AnnounceResult(Guid enrolmentId, int score, DateTimeOffset now) =>
        Raise(new ExamResultRecordedDomainEvent(Id, TenantId, ClassId, enrolmentId, score, now));
}

/// <summary>
/// One student's mark in one exam.
/// </summary>
/// <remarks>
/// Outside the <see cref="Exam"/> aggregate for the same reason attendance is
/// outside the enrolment, and held to one row per student per exam by a unique
/// index on <c>(ExamId, EnrolmentId)</c>. Two marks for one child in one exam
/// would make the average score - which the progress view reports - depend on
/// which row happened to be read.
///
/// The grade is stored rather than derived. A Pathshala's grade bands are its
/// own and may change between sessions; deriving on read would silently rewrite
/// last year's grades when this year's bands were edited.
/// </remarks>
public sealed class ExamResult : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ExamId { get; private set; }
    public Guid EnrolmentId { get; private set; }
    public int Score { get; private set; }
    public string? Grade { get; private set; }
    public Guid RecordedByMemberId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    private ExamResult() { }   // EF Core

    public ExamResult(
        Guid tenantId,
        Guid examId,
        Guid enrolmentId,
        int score,
        string? grade,
        Guid recordedByMemberId,
        DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        ExamId = examId;
        EnrolmentId = enrolmentId;
        Score = score;
        Grade = string.IsNullOrWhiteSpace(grade) ? null : grade.Trim();
        RecordedByMemberId = recordedByMemberId;
        RecordedAt = now;
    }

    /// <summary>Corrects a mark already recorded.</summary>
    public void Amend(int score, string? grade, Guid recordedByMemberId, DateTimeOffset now)
    {
        Score = score;
        Grade = string.IsNullOrWhiteSpace(grade) ? null : grade.Trim();
        RecordedByMemberId = recordedByMemberId;
        RecordedAt = now;
    }
}
