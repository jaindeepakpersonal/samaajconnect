namespace Sangam.VolunteerGroups.Application.Abstractions;

/// <summary>
/// Injected rather than calling DateTimeOffset.UtcNow directly so handler
/// tests can assert on timestamps without sleeping or tolerancing.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
