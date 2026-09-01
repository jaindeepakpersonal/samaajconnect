using Sangam.Boli.Application.Abstractions;

namespace Sangam.Boli.IntegrationTests;

/// <summary>
/// A clock the tests can move.
/// </summary>
/// <remarks>
/// A Boli takes bids only while its window is open, and stops the moment the
/// window passes - AcceptsBids checks the clock as well as the status,
/// deliberately, so a Boli nobody closed does not keep taking bids.
///
/// So a test that needs bidding to end has to move past the closing time.
/// Sleeping would make the suite slow and flaky; moving the clock makes it
/// exact.
/// </remarks>
public sealed class TestClock : IDateTimeProvider
{
    private DateTimeOffset _now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

    public void Set(DateTimeOffset now) => _now = now;
}
