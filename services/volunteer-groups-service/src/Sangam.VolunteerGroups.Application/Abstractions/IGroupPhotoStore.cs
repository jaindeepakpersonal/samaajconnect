using Sangam.VolunteerGroups.Domain.Media;

namespace Sangam.VolunteerGroups.Application.Abstractions;

/// <summary>
/// Where group photos' bytes live - this service's own database, tenant
/// filtered like every other table here. See <see cref="GroupPhoto"/> for why.
/// </summary>
public interface IGroupPhotoStore
{
    Task<GroupPhoto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(GroupPhoto photo);

    /// <summary>Removes a photo. Silent when the id is unknown.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
