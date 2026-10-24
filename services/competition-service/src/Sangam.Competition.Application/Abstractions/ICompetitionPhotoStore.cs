using Sangam.Competition.Domain.Media;

namespace Sangam.Competition.Application.Abstractions;

/// <summary>
/// Where competition photos' bytes live - this service's own database, tenant
/// filtered like every other table here. See <see cref="CompetitionPhoto"/> for why.
/// </summary>
public interface ICompetitionPhotoStore
{
    Task<CompetitionPhoto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(CompetitionPhoto photo);

    /// <summary>Removes a photo. Silent when the id is unknown.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
