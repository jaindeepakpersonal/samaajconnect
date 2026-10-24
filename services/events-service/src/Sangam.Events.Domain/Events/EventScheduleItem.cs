namespace Sangam.Events.Domain.Events;

/// <summary>
/// One programme on an event's schedule - "Day 1, 9:00-10:30, Prabhat Pheri" -
/// for an event that runs as several sessions over one or more days.
/// </summary>
/// <remarks>
/// <para>
/// A date and wall-clock times rather than instants. A schedule is read the way
/// it is printed on a pamphlet: the date the programme is on, and the time on
/// the clock where it happens. The event's own <c>StartAt</c> is an instant
/// because it orders and filters the list; nothing compares a programme's time
/// across time zones, so storing one would only invite converting it twice.
/// </para>
/// <para>
/// Owned by <see cref="SamaajEvent"/>, changed only through its methods, and
/// not tenant-scoped of its own - it is reached only through its event, which is.
/// </para>
/// </remarks>
public sealed class EventScheduleItem
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public DateOnly Date { get; private set; }
    public string ProgramName { get; private set; } = null!;
    public TimeOnly StartTime { get; private set; }

    /// <summary>Optional: a programme often has a start and no fixed end.</summary>
    public TimeOnly? EndTime { get; private set; }

    private EventScheduleItem() { }   // EF Core

    internal EventScheduleItem(Guid eventId, DateOnly date, string programName, TimeOnly startTime, TimeOnly? endTime)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        Set(date, programName, startTime, endTime);
    }

    internal void Set(DateOnly date, string programName, TimeOnly startTime, TimeOnly? endTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programName);

        if (endTime is { } end && end <= startTime)
        {
            throw new ArgumentException("A programme cannot end before it starts.", nameof(endTime));
        }

        Date = date;
        ProgramName = programName.Trim();
        StartTime = startTime;
        EndTime = endTime;
    }
}
