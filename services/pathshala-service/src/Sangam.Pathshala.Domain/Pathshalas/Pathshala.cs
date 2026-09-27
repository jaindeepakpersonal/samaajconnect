using Sangam.Pathshala.Domain.Common;
using Sangam.Pathshala.Domain.Pathshalas.Events;

namespace Sangam.Pathshala.Domain.Pathshalas;

/// <summary>
/// A Jain Pathshala: the school itself, its academic sessions, and its classes.
/// </summary>
/// <remarks>
/// <b>What this aggregate holds is bounded on purpose.</b> Sessions, classes,
/// their schedules and their teachers are a handful of rows each and change
/// rarely, so loading them together to answer "what does this Pathshala run?"
/// costs nothing.
///
/// Enrolments, attendance and exam results are <i>not</i> here, and that is the
/// central shape of this service. One class of twenty-five students generates
/// roughly twelve hundred attendance rows in a year; a Pathshala with eight
/// classes generates ten thousand. Pulling those into the aggregate would mean
/// reading the year to mark one register. They are separate roots, written
/// directly, with database indexes rather than in-memory checks holding their
/// uniqueness - the same decision, for the same reason, as
/// celebrity-voting-service made about votes.
///
/// The master record is created by a Super Admin (DATA-MODEL.md section 9) and
/// operated by the Samaaj. Both are expressed as permissions rather than as
/// anything this aggregate knows about.
/// </remarks>
public sealed class Pathshala : AggregateRoot, ITenantScopedEntity
{
    private readonly List<AcademicSession> _sessions = [];
    private readonly List<PathshalaClass> _classes = [];
    private readonly List<Teacher> _teachers = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public PathshalaAddress? Address { get; private set; }
    public string? ContactPerson { get; private set; }
    public PathshalaStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<AcademicSession> Sessions => _sessions.AsReadOnly();

    public IReadOnlyCollection<PathshalaClass> Classes => _classes.AsReadOnly();

    public IReadOnlyCollection<Teacher> Teachers => _teachers.AsReadOnly();

    private Pathshala() { }   // EF Core

    public static Pathshala Create(
        Guid tenantId,
        string name,
        PathshalaAddress? address,
        string? contactPerson,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var pathshala = new Pathshala
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Address = address,
            ContactPerson = Blank(contactPerson),
            Status = PathshalaStatus.Active,
            CreatedAt = now,
        };

        pathshala.Raise(new PathshalaCreatedDomainEvent(pathshala.Id, tenantId, now));

        return pathshala;
    }

    /// <summary>
    /// Corrects the name, address and contact person, replacing all three.
    /// </summary>
    /// <remarks>
    /// Allowed on a stood-down Pathshala too: its records are kept, and a
    /// record kept under a misspelt name or the wrong address is worth
    /// correcting. The address is also what decides which Area Admin runs it,
    /// so moving it is the one way a Pathshala created before localities
    /// mattered becomes a locality's to run.
    /// </remarks>
    public void UpdateDetails(
        string name, PathshalaAddress? address, string? contactPerson, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Address = address;
        ContactPerson = Blank(contactPerson);

        Raise(new PathshalaUpdatedDomainEvent(Id, TenantId, now));
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public AcademicSession? FindSession(Guid sessionId) =>
        _sessions.FirstOrDefault(s => s.Id == sessionId);

    public PathshalaClass? FindClass(Guid classId) =>
        _classes.FirstOrDefault(c => c.Id == classId);

    /// <summary>The session enrolments and classes default to.</summary>
    public AcademicSession? CurrentSession => _sessions.FirstOrDefault(s => s.IsCurrent);

    /// <summary>
    /// Opens an academic session and makes it the current one.
    /// </summary>
    /// <remarks>
    /// Exactly one session is current, so opening a new one closes the previous
    /// one's tenure rather than leaving two. "Current" is what decides where a
    /// new enrolment lands, and two answers to that is worse than none: a child
    /// enrolled into last year would look enrolled and appear on no register.
    ///
    /// The old session's records are untouched. A session ending is not a
    /// reason to lose the attendance taken during it.
    /// </remarks>
    public AcademicSession OpenSession(
        string label, DateOnly startDate, DateOnly endDate, DateTimeOffset now)
    {
        foreach (var existing in _sessions)
        {
            existing.StandDown();
        }

        var session = new AcademicSession(Id, label, startDate, endDate, isCurrent: true);

        _sessions.Add(session);

        Raise(new AcademicSessionOpenedDomainEvent(Id, TenantId, session.Id, label, now));

        return session;
    }

    /// <summary>
    /// Adds a class to a session. Returns null when there is no such session.
    /// </summary>
    public PathshalaClass? AddClass(Guid sessionId, string name, string? roomLabel)
    {
        if (FindSession(sessionId) is null)
        {
            return null;
        }

        var pathshalaClass = new PathshalaClass(Id, sessionId, name, roomLabel);

        _classes.Add(pathshalaClass);

        return pathshalaClass;
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (Status == PathshalaStatus.Inactive)
        {
            return;
        }

        Status = PathshalaStatus.Inactive;

        Raise(new PathshalaDeactivatedDomainEvent(Id, TenantId, now));
    }

    /// <summary>Whether this Pathshala is taking new enrolments.</summary>
    public bool AcceptsEnrolments => Status == PathshalaStatus.Active && CurrentSession is not null;

    // ---- Teachers ------------------------------------------------------------

    public Teacher? FindTeacher(Guid teacherId) => _teachers.FirstOrDefault(t => t.Id == teacherId);

    /// <summary>The teacher record signed in as <paramref name="userId"/>, if any.</summary>
    public Teacher? TeacherFor(Guid userId) => _teachers.FirstOrDefault(t => t.UserId == userId);

    /// <summary>
    /// Whether <paramref name="userId"/> is a teacher here at all - the coarse
    /// check for Pathshala staff, e.g. who may send its parents an announcement.
    /// </summary>
    public bool IsTeacher(Guid userId) => TeacherFor(userId) is not null;

    /// <summary>
    /// Whether <paramref name="userId"/> teaches this particular class.
    /// </summary>
    /// <remarks>
    /// This is the check that decides who may mark a register or record a
    /// result, and it is deliberately about <i>this</i> class rather than about
    /// holding the teacher role. <c>Pathshala.Attendance.Write</c> says somebody
    /// is a teacher somewhere; it does not say whose attendance they may write,
    /// and treating it as though it did would let any teacher in the Samaaj mark
    /// any class in any Pathshala. Resolved through the teacher record's linked
    /// account, so a teacher without a login teaches nothing as far as access
    /// goes - an administrator acts for them.
    /// </remarks>
    public bool Teaches(Guid userId, PathshalaClass pathshalaClass) =>
        TeacherFor(userId) is { } teacher && pathshalaClass.HasTeacher(teacher.Id);

    /// <summary>
    /// Adds a teacher. Null when <paramref name="userId"/> is already another
    /// teacher here - one account is one teacher, or "which of these two is
    /// signed in?" has no answer.
    /// </summary>
    public Teacher? AddTeacher(
        string fullName,
        string? phone,
        string? email,
        string? qualification,
        Guid? userId,
        Guid changedBy,
        DateTimeOffset now)
    {
        if (userId is { } account && TeacherFor(account) is not null)
        {
            return null;
        }

        var teacher = new Teacher(Id, fullName, phone, email, qualification, userId, now);

        _teachers.Add(teacher);

        Raise(new TeacherAddedDomainEvent(Id, TenantId, teacher.Id, changedBy, now));

        return teacher;
    }

    /// <summary>Corrects a teacher's details or login. The same one-account-one-teacher rule.</summary>
    public TeacherChange UpdateTeacher(
        Guid teacherId,
        string fullName,
        string? phone,
        string? email,
        string? qualification,
        Guid? userId,
        Guid changedBy,
        DateTimeOffset now)
    {
        var teacher = FindTeacher(teacherId);

        if (teacher is null)
        {
            return TeacherChange.NotFound;
        }

        if (userId is { } account && TeacherFor(account) is { } other && other.Id != teacherId)
        {
            return TeacherChange.AccountTaken;
        }

        teacher.Change(fullName, phone, email, qualification, userId);

        Raise(new TeacherUpdatedDomainEvent(Id, TenantId, teacherId, changedBy, now));

        return TeacherChange.Done;
    }

    /// <summary>
    /// Removes a teacher, and with them every class assignment they held. The
    /// registers they marked keep who marked them.
    /// </summary>
    public bool RemoveTeacher(Guid teacherId, Guid changedBy, DateTimeOffset now)
    {
        var teacher = FindTeacher(teacherId);

        if (teacher is null)
        {
            return false;
        }

        foreach (var pathshalaClass in _classes)
        {
            pathshalaClass.RemoveTeacher(teacherId);
        }

        _teachers.Remove(teacher);

        Raise(new TeacherRemovedDomainEvent(Id, TenantId, teacherId, changedBy, now));

        return true;
    }
}

public enum TeacherChange
{
    Done,
    NotFound,

    /// <summary>The login is already another teacher's here.</summary>
    AccountTaken,
}

public enum PathshalaStatus
{
    Active = 1,
    Inactive = 2,
}
