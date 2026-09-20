using Sangam.Trust.Application.Abstractions;

namespace Sangam.Trust.IntegrationTests;

public sealed class TestClock : IDateTimeProvider
{
    private DateTimeOffset _now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

    public void Set(DateTimeOffset now) => _now = now;
}
