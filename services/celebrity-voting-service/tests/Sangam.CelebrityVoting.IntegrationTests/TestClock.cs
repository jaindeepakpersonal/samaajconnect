using Sangam.CelebrityVoting.Application.Abstractions;

namespace Sangam.CelebrityVoting.IntegrationTests;

/// <summary>
/// A clock the tests can move.
/// </summary>
/// <remarks>
/// This service is the first with behaviour that depends on real elapsed time
/// in a way tests cannot fake with dates alone. A campaign will not take
/// nominations and votes at the same moment - the validator refuses a voting
/// window that starts before nominations close, deliberately, because otherwise
/// people who vote early see a different ballot from people who vote late.
///
/// So a test that needs an open ballot has to nominate at one time and vote at
/// a later one. Sleeping would make the suite slow and flaky; moving the clock
/// makes it exact.
/// </remarks>
public sealed class TestClock : IDateTimeProvider
{
    private DateTimeOffset _now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

    public void Set(DateTimeOffset now) => _now = now;
}
