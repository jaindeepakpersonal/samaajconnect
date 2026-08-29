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

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public string? ContactPerson { get; private set; }
    public PathshalaStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<AcademicSession> Sessions => _sessions.AsReadOnly();

    public IReadOnlyCollection<PathshalaClass> Classes => _classes.AsReadOnly();

    private Pathshala() { }   // EF Core

    public static Pathshala Create(
        Guid tenantId,
        string name,
        string? address,
        string? contactPerson,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var pathshala = new Pathshala
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Address = Blank(address),
            ContactPerson = Blank(contactPerson),
            Status = PathshalaStatus.Active,
            CreatedAt = now,
        };

        pathshala.Raise(new PathshalaCreatedDomainEvent(pathshala.Id, tenantId, now));

        return pathshala;
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

    /// <summary>Whether <paramref name="memberId"/> teaches any class here.</summary>
    /// <remarks>
    /// The coarse check, used to decide whether somebody is Pathshala staff at
    /// all. Whether they may mark <i>this</i> register is a per-class question -
    /// see <see cref="PathshalaClass.IsTaughtBy"/>.
    /// </remarks>
    public bool IsTeacher(Guid memberId) => _classes.Any(c => c.IsTaughtBy(memberId));
}

public enum PathshalaStatus
{
    Active = 1,
    Inactive = 2,
}
