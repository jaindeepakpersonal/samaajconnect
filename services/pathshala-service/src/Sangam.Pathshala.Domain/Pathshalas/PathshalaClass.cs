namespace Sangam.Pathshala.Domain.Pathshalas;

/// <summary>An academic session, e.g. "2026-27".</summary>
public sealed class AcademicSession
{
    public Guid Id { get; private set; }
    public Guid PathshalaId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    /// <summary>
    /// Exactly one session per Pathshala carries this. See
    /// <see cref="Pathshala.OpenSession"/>.
    /// </summary>
    public bool IsCurrent { get; private set; }

    private AcademicSession() { }   // EF Core

    internal AcademicSession(
        Guid pathshalaId, string label, DateOnly startDate, DateOnly endDate, bool isCurrent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Id = Guid.NewGuid();
        PathshalaId = pathshalaId;
        Label = label.Trim();
        StartDate = startDate;
        EndDate = endDate;
        IsCurrent = isCurrent;
    }

    internal void StandDown() => IsCurrent = false;

    /// <summary>Corrects the label and dates. Whether it is current is not a detail - opening a session decides that.</summary>
    internal void Correct(string label, DateOnly startDate, DateOnly endDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Label = label.Trim();
        StartDate = startDate;
        EndDate = endDate;
    }
}

/// <summary>
/// One class within a session, with its weekly schedule and its teachers.
/// </summary>
/// <remarks>
/// Named <c>PathshalaClass</c> rather than <c>Class</c>, which is a C# keyword.
/// The table is <c>classes</c> and the API says "class"; only the CLR type
/// carries the prefix.
/// </remarks>
public sealed class PathshalaClass
{
    private readonly List<ClassSchedule> _schedule = [];
    private readonly List<TeacherAssignment> _teachers = [];

    public Guid Id { get; private set; }
    public Guid PathshalaId { get; private set; }
    public Guid SessionId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? RoomLabel { get; private set; }

    public IReadOnlyCollection<ClassSchedule> Schedule => _schedule.AsReadOnly();

    public IReadOnlyCollection<TeacherAssignment> Teachers => _teachers.AsReadOnly();

    private PathshalaClass() { }   // EF Core

    internal PathshalaClass(Guid pathshalaId, Guid sessionId, string name, string? roomLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.NewGuid();
        PathshalaId = pathshalaId;
        SessionId = sessionId;
        Name = name.Trim();
        RoomLabel = string.IsNullOrWhiteSpace(roomLabel) ? null : roomLabel.Trim();
    }

    /// <summary>
    /// Whether this <see cref="Teacher"/> record teaches this class. Who is
    /// signed in as them is <see cref="Pathshala.Teaches"/>'s question.
    /// </summary>
    public bool HasTeacher(Guid teacherId) => _teachers.Any(t => t.TeacherId == teacherId);

    /// <summary>Assigns a teacher. A repeat assignment is a no-op.</summary>
    public bool AssignTeacher(Guid teacherId, DateTimeOffset now)
    {
        if (HasTeacher(teacherId))
        {
            return false;
        }

        _teachers.Add(new TeacherAssignment(Id, teacherId, now));

        return true;
    }

    public bool RemoveTeacher(Guid teacherId)
    {
        var assignment = _teachers.FirstOrDefault(t => t.TeacherId == teacherId);

        if (assignment is null)
        {
            return false;
        }

        _teachers.Remove(assignment);

        return true;
    }

    /// <summary>
    /// Adds a weekly slot. Returns false when the class already meets then.
    /// </summary>
    /// <remarks>
    /// Overlap is checked rather than merely exact duplication: two slots on the
    /// same day that overlap describe a class that meets twice at once, which
    /// nothing downstream can render sensibly.
    /// </remarks>
    public bool AddSlot(DayOfWeek day, TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
        {
            return false;
        }

        var clashes = _schedule.Any(s =>
            s.DayOfWeek == day && startTime < s.EndTime && s.StartTime < endTime);

        if (clashes)
        {
            return false;
        }

        _schedule.Add(new ClassSchedule(Id, day, startTime, endTime));

        return true;
    }

    /// <summary>Whether the class meets on <paramref name="date"/>.</summary>
    /// <remarks>
    /// Used to refuse a register for a day the class does not meet. A Pathshala
    /// that has not set a schedule yet is not blocked by this - an empty
    /// schedule means "unknown", not "never".
    /// </remarks>
    public bool MeetsOn(DateOnly date) =>
        _schedule.Count == 0 || _schedule.Any(s => s.DayOfWeek == date.DayOfWeek);
}

public sealed class ClassSchedule
{
    public Guid Id { get; private set; }
    public Guid ClassId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }

    private ClassSchedule() { }   // EF Core

    internal ClassSchedule(Guid classId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        Id = Guid.NewGuid();
        ClassId = classId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
    }
}

/// <summary>A <see cref="Teacher"/> teaching one class.</summary>
public sealed class TeacherAssignment
{
    public Guid Id { get; private set; }
    public Guid ClassId { get; private set; }
    public Guid TeacherId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    private TeacherAssignment() { }   // EF Core

    internal TeacherAssignment(Guid classId, Guid teacherId, DateTimeOffset assignedAt)
    {
        Id = Guid.NewGuid();
        ClassId = classId;
        TeacherId = teacherId;
        AssignedAt = assignedAt;
    }
}
