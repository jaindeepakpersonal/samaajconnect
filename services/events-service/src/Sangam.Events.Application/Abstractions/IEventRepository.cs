using Sangam.Events.Domain.Events;

namespace Sangam.Events.Application.Abstractions;

public interface IEventRepository
{
    /// <summary>Tenant-filtered, with registrations loaded.</summary>
    Task<SamaajEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// This Samaaj's events, soonest first.
    /// </summary>
    /// <remarks>
    /// <paramref name="publishedOnly"/> is what separates the member's list
    /// from the organiser's: a draft is not an event anyone has been told
    /// about, and a member seeing one would be seeing something that may never
    /// happen.
    /// </remarks>
    Task<IReadOnlyList<SamaajEvent>> ListAsync(
        bool publishedOnly,
        bool upcomingOnly,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    void Add(SamaajEvent samaajEvent);
}
