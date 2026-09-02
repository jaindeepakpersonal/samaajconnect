using Sangam.Pathshala.Domain.Enrolments;
using Sangam.Pathshala.Domain.Pathshalas;

namespace Sangam.Pathshala.Application.Abstractions;

/// <summary>
/// The Pathshala and everything about how it is organised.
/// </summary>
/// <remarks>
/// Sessions, classes, schedules and teachers are loaded together, because they
/// are a handful of rows and every question about a Pathshala needs several of
/// them at once. Enrolments, attendance and results are never loaded here - see
/// <see cref="IEnrolmentRepository"/> and the remarks on
/// <see cref="Domain.Pathshalas.Pathshala"/>.
/// </remarks>
public interface IPathshalaRepository
{
    Task<Domain.Pathshalas.Pathshala?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default);

    /// <summary>The Pathshala that owns <paramref name="classId"/>, or null.</summary>
    Task<Domain.Pathshalas.Pathshala?> GetByClassIdAsync(
        Guid classId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Domain.Pathshalas.Pathshala>> ListAsync(
        CancellationToken cancellationToken = default);

    void Add(Domain.Pathshalas.Pathshala pathshala);
}

/// <summary>
/// Enrolments, and the attendance and results hanging off them.
/// </summary>
/// <remarks>
/// The counting methods return aggregates computed in the database rather than
/// collections to be counted in memory. A class of twenty-five over a year is
/// more than a thousand attendance rows, and the screens that want them want a
/// percentage.
/// </remarks>
public interface IEnrolmentRepository
{
    Task<StudentEnrolment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every enrolment at a Pathshala, optionally narrowed to one status.</summary>
    Task<IReadOnlyList<StudentEnrolment>> ListForPathshalaAsync(
        Guid pathshalaId, EnrolmentStatus? status, CancellationToken cancellationToken = default);

    /// <summary>The roll for one class: placed students only.</summary>
    Task<IReadOnlyList<StudentEnrolment>> ListForClassAsync(
        Guid classId, CancellationToken cancellationToken = default);

    /// <summary>What this member can see without being Pathshala staff.</summary>
    Task<IReadOnlyList<StudentEnrolment>> ListForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// An existing request for this child at this Pathshala, whatever its state.
    /// </summary>
    /// <remarks>
    /// The courtesy check behind the unique index on
    /// <c>(PathshalaId, ChildProfileId)</c>: it exists so a parent who submits
    /// twice gets an answer rather than a database error.
    /// </remarks>
    Task<StudentEnrolment?> FindForChildAsync(
        Guid pathshalaId, Guid childProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolments belonging to a child profile, for the Kafka consumer.
    /// </summary>
    /// <remarks>
    /// <b>Reads past the global tenant filter, and has to.</b> A consumer has no
    /// request and so no resolved tenant, which makes
    /// <c>ITenantContext.TenantId</c> null and the filter compare every row
    /// against <c>Guid.Empty</c> - so a filtered read finds nothing and the
    /// conversion link silently does not happen. That is exactly how it failed
    /// the first time it was tested.
    ///
    /// The bypass is not unscoped: <paramref name="tenantId"/> comes off the
    /// event, which this platform published through its own outbox, and is
    /// applied explicitly here instead. Do not call this from a request path -
    /// a request has a tenant, and the filter is the right thing there.
    /// </remarks>
    Task<IReadOnlyList<StudentEnrolment>> ListForChildAsync(
        Guid tenantId, Guid childProfileId, CancellationToken cancellationToken = default);

    void Add(StudentEnrolment enrolment);

    // ---- Attendance -------------------------------------------------------

    Task<IReadOnlyList<AttendanceEntry>> ListAttendanceForEnrolmentAsync(
        Guid enrolmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a whole register - reading what is already there, amending it, and
    /// inserting the rest - entirely on its own scope and connection.
    /// </summary>
    /// <remarks>
    /// <b>The read and the write are together here on purpose, and the first
    /// version of this got it wrong.</b> Splitting them - amending on the
    /// request's context and inserting on a separate one - means the request's
    /// transaction holds row locks that the second connection then waits for,
    /// so two teachers submitting the same register deadlock each other. It
    /// showed up as a test that passed alone and failed about one run in three.
    ///
    /// One connection, outside the request's transaction, also gets the two
    /// things the separate scope was for in the first place. A register is
    /// twenty-five writes sent at once and often sent twice, so holding the
    /// request's transaction across them serialises teachers against each
    /// other; and a unique violation poisons the change tracker it lands on,
    /// which on the request's context would turn one duplicated row into a
    /// failure of everything after it.
    /// </remarks>
    Task<RegisterOutcome> SaveRegisterAsync(
        Guid classId,
        DateOnly classDate,
        IReadOnlyList<RegisterMark> marks,
        Guid markedByMemberId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Present/absent/excused counts per enrolment, counted in the database.</summary>
    Task<AttendanceTally> TallyAttendanceAsync(
        Guid enrolmentId, CancellationToken cancellationToken = default);

    /// <summary>One class's register for one date, as it currently stands.</summary>
    /// <remarks>
    /// <b>The register was writable and not readable, which made amending it a
    /// guess.</b> Re-marking a date amends what is already there, so a teacher
    /// correcting one child's mark had no way to see the other twenty-four -
    /// they had to be re-entered from memory, or left to be overwritten with
    /// whatever a blank form defaulted to. Reading the marks back is what makes
    /// the amend path safe to offer at all.
    /// </remarks>
    Task<IReadOnlyList<AttendanceEntry>> ListRegisterAsync(
        Guid classId, DateOnly classDate, CancellationToken cancellationToken = default);

    // ---- Exams ------------------------------------------------------------

    Task<Exam?> GetExamAsync(Guid examId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Exam>> ListExamsForClassAsync(
        Guid classId, CancellationToken cancellationToken = default);

    void AddExam(Exam exam);

    Task<ExamResult?> FindResultAsync(
        Guid examId, Guid enrolmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExamResult>> ListResultsForEnrolmentAsync(
        Guid enrolmentId, CancellationToken cancellationToken = default);

    /// <summary>Every mark recorded in any of one class's exams.</summary>
    /// <remarks>
    /// Read for the whole class rather than per exam: a class has a handful of
    /// exams and a handful of students, and one round trip beats one per exam
    /// for a screen that shows them together.
    /// </remarks>
    Task<IReadOnlyList<ExamResult>> ListResultsForClassAsync(
        Guid classId, CancellationToken cancellationToken = default);

    void AddResult(ExamResult result);
}

/// <summary>One student's mark, resolved against the roll and ready to write.</summary>
public sealed record RegisterMark(Guid EnrolmentId, Guid TenantId, AttendanceStatus Status);

/// <summary>
/// What a register submission did: rows written for the first time, and rows
/// that already existed and were corrected.
/// </summary>
/// <remarks>
/// A teacher who submits twice should be able to see that the second submission
/// corrected rather than added, which is the difference between the index doing
/// its job and the register being silently duplicated.
/// </remarks>
public sealed record RegisterOutcome(int Recorded, int Amended);

/// <summary>
/// How one student's attendance stands. Excused days are counted separately
/// because they are not held against the student, so the percentage is over
/// present and absent only - a child excused for half a term should not appear
/// to have a poor record.
/// </summary>
public sealed record AttendanceTally(int Present, int Absent, int Excused)
{
    public int Recorded => Present + Absent + Excused;

    /// <summary>
    /// Null when nothing counts yet. Null rather than zero, because zero is a
    /// claim - that the student attended nothing - and it would be the wrong
    /// one in the week before a session starts.
    /// </summary>
    public int? Percentage =>
        Present + Absent == 0 ? null : (int)Math.Round(100.0 * Present / (Present + Absent));
}
