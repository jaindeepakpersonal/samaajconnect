using Sangam.Pathshala.Application.Abstractions;

namespace Sangam.Pathshala.IntegrationTests;

/// <summary>
/// A clock the tests can move.
/// </summary>
/// <remarks>
/// Attendance is refused for a date the class has not met yet, which is a rule
/// worth having and a nuisance to test against the wall clock: a register
/// marked "today" is fine until the suite runs just before midnight UTC.
/// Moving the clock makes every date in these tests exact.
/// </remarks>
public sealed class TestClock : IDateTimeProvider
{
    private DateTimeOffset _now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

    public void Set(DateTimeOffset now) => _now = now;
}
