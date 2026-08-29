using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Domain.Enrolments;
using Sangam.Pathshala.Infrastructure.Persistence;

namespace Sangam.Pathshala.Infrastructure.Repositories;

public sealed class PathshalaRepository(PathshalaDbContext dbContext) : IPathshalaRepository
{
    /// <summary>
    /// Sessions, classes, schedules and teachers together. Never enrolments -
    /// see IPathshalaRepository.
    /// </summary>
    private IQueryable<Domain.Pathshalas.Pathshala> Full =>
        dbContext.Pathshalas
            .Include(p => p.Sessions)
            .Include(p => p.Classes).ThenInclude(c => c.Schedule)
            .Include(p => p.Classes).ThenInclude(c => c.Teachers);

    public Task<Domain.Pathshalas.Pathshala?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        Full.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Domain.Pathshalas.Pathshala?> GetByClassIdAsync(
        Guid classId, CancellationToken cancellationToken = default) =>
        Full.FirstOrDefaultAsync(p => p.Classes.Any(c => c.Id == classId), cancellationToken);

    public async Task<IReadOnlyList<Domain.Pathshalas.Pathshala>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await Full.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public void Add(Domain.Pathshalas.Pathshala pathshala) => dbContext.Pathshalas.Add(pathshala);
}

public sealed class EnrolmentRepository(
    PathshalaDbContext dbContext, IServiceScopeFactory scopeFactory)
    : IEnrolmentRepository
{
    /// <summary>Postgres unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>Postgres deadlock SQLSTATE.</summary>
    private const string Deadlock = "40P01";

    /// <summary>
    /// Whether an exception is one concurrent registers legitimately produce.
    /// </summary>
    /// <remarks>
    /// Two SQLSTATEs, and the second one cost an afternoon. A unique violation
    /// is the expected collision: another submission wrote this mark first.
    /// A <b>deadlock</b> is what a batch of them produces - ten copies of one
    /// register each insert the same five keys in one transaction, and two
    /// transactions that take those locks in different orders wait on each
    /// other until Postgres kills one. It surfaced as a plain 500 on a test
    /// that passed alone and failed about one run in three.
    ///
    /// Both are handled the same way: give up on the batch and write the marks
    /// one at a time, where a transaction holds one lock at a time and no cycle
    /// is possible.
    ///
    /// <b>The parameter is Exception, not DbUpdateException, and that is the
    /// second half of the same bug.</b> EF surfaces a unique violation as a
    /// <c>DbUpdateException</c> directly, but classifies a deadlock as transient
    /// and rewraps it in an <c>InvalidOperationException</c> - "An exception has
    /// been raised that is likely due to a transient failure". A catch typed to
    /// <c>DbUpdateException</c> therefore handles one and silently misses the
    /// other, which is exactly what the first fix here did. Walk the chain
    /// instead of trusting the outermost type.
    /// </remarks>
    private static bool IsContention(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: UniqueViolation or Deadlock })
            {
                return true;
            }
        }

        return false;
    }

    public Task<StudentEnrolment?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Enrolments.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<StudentEnrolment>> ListForPathshalaAsync(
        Guid pathshalaId, EnrolmentStatus? status, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Enrolments.AsNoTracking().Where(e => e.PathshalaId == pathshalaId);

        if (status is { } wanted)
        {
            query = query.Where(e => e.Status == wanted);
        }

        return await query.OrderBy(e => e.RequestedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StudentEnrolment>> ListForClassAsync(
        Guid classId, CancellationToken cancellationToken = default) =>
        await dbContext.Enrolments
            .AsNoTracking()
            .Where(e => e.ClassId == classId && e.Status == EnrolmentStatus.Active)
            .OrderBy(e => e.EnrolledAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StudentEnrolment>> ListForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default) =>
        await dbContext.Enrolments
            .AsNoTracking()
            .Where(e => e.RequestedByMemberId == memberId || e.StudentUserId == memberId)
            .OrderByDescending(e => e.RequestedAt)
            .ToListAsync(cancellationToken);

    public Task<StudentEnrolment?> FindForChildAsync(
        Guid pathshalaId, Guid childProfileId, CancellationToken cancellationToken = default) =>
        dbContext.Enrolments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.PathshalaId == pathshalaId && e.ChildProfileId == childProfileId,
                cancellationToken);

    public async Task<IReadOnlyList<StudentEnrolment>> ListForChildAsync(
        Guid tenantId, Guid childProfileId, CancellationToken cancellationToken = default) =>
        await dbContext.Enrolments
            // Past the filter, with the tenant applied by hand. The consumer has
            // no request and so no resolved tenant; a filtered read here
            // compares every row against Guid.Empty and finds nothing. See
            // IEnrolmentRepository.
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.ChildProfileId == childProfileId)

            // Tracked: the conversion consumer amends these.
            .ToListAsync(cancellationToken);

    public void Add(StudentEnrolment enrolment) => dbContext.Enrolments.Add(enrolment);

    // ---- Attendance -------------------------------------------------------

    public async Task<IReadOnlyList<AttendanceEntry>> ListAttendanceForEnrolmentAsync(
        Guid enrolmentId, CancellationToken cancellationToken = default) =>
        await dbContext.Attendance
            .AsNoTracking()
            .Where(a => a.EnrolmentId == enrolmentId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Reads, amends and inserts the register on one connection of its own. See
    /// IEnrolmentRepository for why the read and the write belong together.
    /// </summary>
    /// <remarks>
    /// The batch is attempted in one round trip, which is the normal case. A
    /// unique violation aborts the whole batch and poisons that context, so the
    /// fallback re-reads and retries row by row on fresh contexts: a register
    /// colliding with a simultaneous copy of itself must still record the marks
    /// that were not duplicates, and must report the ones it lost as corrections
    /// rather than as failures.
    /// </remarks>
    public async Task<RegisterOutcome> SaveRegisterAsync(
        Guid classId,
        DateOnly classDate,
        IReadOnlyList<RegisterMark> marks,
        Guid markedByMemberId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<PathshalaDbContext>();

            var outcome = await ApplyAsync(
                context, classId, classDate, marks, markedByMemberId, now, cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            return outcome;
        }
        catch (Exception exception) when (IsContention(exception))
        {
            // Another submission of this register collided with this one -
            // either it wrote a mark first, or the two batches deadlocked
            // taking the same locks. Retry each mark on its own context, where
            // one lock is held at a time and no cycle is possible.
        }

        var recorded = 0;
        var amended = 0;

        foreach (var mark in marks)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<PathshalaDbContext>();

            try
            {
                var outcome = await ApplyAsync(
                    context, classId, classDate, [mark], markedByMemberId, now, cancellationToken);

                await context.SaveChangesAsync(cancellationToken);

                recorded += outcome.Recorded;
                amended += outcome.Amended;
            }
            catch (Exception exception) when (IsContention(exception))
            {
                // The index refused it: the other submission wrote this mark.
                // The register still says what the teacher meant, so this counts
                // as a correction rather than a loss.
                amended++;
            }
        }

        return new RegisterOutcome(recorded, amended);
    }

    /// <summary>
    /// Amends the marks already there and adds the rest, without saving.
    /// </summary>
    /// <remarks>
    /// Reads past the query filter and applies the tenant from the roll instead.
    /// The rows being amended were resolved against enrolments the handler
    /// already checked belong to this Samaaj, and the filter would otherwise
    /// hide them from a context created on a fresh scope, where nothing has
    /// populated ITenantContext from the request.
    /// </remarks>
    private static async Task<RegisterOutcome> ApplyAsync(
        PathshalaDbContext context,
        Guid classId,
        DateOnly classDate,
        IReadOnlyList<RegisterMark> marks,
        Guid markedByMemberId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Sorted, so every concurrent submission of this register takes its
        // row locks in the same order. Unsorted batches taking the same locks
        // in different orders is what deadlocks them against each other.
        marks = [.. marks.OrderBy(m => m.EnrolmentId)];

        var wanted = marks.Select(m => m.EnrolmentId).ToList();

        var existing = await context.Attendance
            .IgnoreQueryFilters()
            .Where(a => a.ClassId == classId
                && a.ClassDate == classDate
                && wanted.Contains(a.EnrolmentId))
            .ToDictionaryAsync(a => a.EnrolmentId, cancellationToken);

        var recorded = 0;
        var amended = 0;

        foreach (var mark in marks)
        {
            if (existing.TryGetValue(mark.EnrolmentId, out var already))
            {
                already.Amend(mark.Status, markedByMemberId, now);
                amended++;
                continue;
            }

            context.Attendance.Add(new AttendanceEntry(
                mark.TenantId,
                mark.EnrolmentId,
                classId,
                classDate,
                mark.Status,
                markedByMemberId,
                now));

            recorded++;
        }

        return new RegisterOutcome(recorded, amended);
    }

    /// <summary>Counted in the database: a year of a class is a thousand rows.</summary>
    public async Task<AttendanceTally> TallyAttendanceAsync(
        Guid enrolmentId, CancellationToken cancellationToken = default)
    {
        var counts = await dbContext.Attendance
            .AsNoTracking()
            .Where(a => a.EnrolmentId == enrolmentId)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        return new AttendanceTally(
            counts.GetValueOrDefault(AttendanceStatus.Present),
            counts.GetValueOrDefault(AttendanceStatus.Absent),
            counts.GetValueOrDefault(AttendanceStatus.Excused));
    }

    // ---- Exams ------------------------------------------------------------

    public Task<Exam?> GetExamAsync(Guid examId, CancellationToken cancellationToken = default) =>
        dbContext.Exams.FirstOrDefaultAsync(e => e.Id == examId, cancellationToken);

    public async Task<IReadOnlyList<Exam>> ListExamsForClassAsync(
        Guid classId, CancellationToken cancellationToken = default) =>
        await dbContext.Exams
            .AsNoTracking()
            .Where(e => e.ClassId == classId)
            .ToListAsync(cancellationToken);

    public void AddExam(Exam exam) => dbContext.Exams.Add(exam);

    public Task<ExamResult?> FindResultAsync(
        Guid examId, Guid enrolmentId, CancellationToken cancellationToken = default) =>
        dbContext.ExamResults
            // Tracked: correcting a mark amends this row.
            .FirstOrDefaultAsync(
                r => r.ExamId == examId && r.EnrolmentId == enrolmentId, cancellationToken);

    public async Task<IReadOnlyList<ExamResult>> ListResultsForEnrolmentAsync(
        Guid enrolmentId, CancellationToken cancellationToken = default) =>
        await dbContext.ExamResults
            .AsNoTracking()
            .Where(r => r.EnrolmentId == enrolmentId)
            .ToListAsync(cancellationToken);

    public void AddResult(ExamResult result) => dbContext.ExamResults.Add(result);
}
