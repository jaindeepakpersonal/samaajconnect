using Sangam.Events.Domain.Media;

namespace Sangam.Events.Application.Abstractions;

/// <summary>
/// Where event photos' bytes live - this service's own database, tenant
/// filtered like every other table here. See <see cref="EventPhoto"/> for why.
/// </summary>
public interface IEventPhotoStore
{
    Task<EventPhoto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(EventPhoto photo);

    /// <summary>Removes a photo. Silent when the id is unknown.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
