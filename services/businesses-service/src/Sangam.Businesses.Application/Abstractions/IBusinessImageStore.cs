using Sangam.Businesses.Domain.Media;

namespace Sangam.Businesses.Application.Abstractions;

/// <summary>
/// Where business and product images' bytes live - this service's own
/// database, tenant filtered like every other table here. See
/// <see cref="BusinessImage"/> for why.
/// </summary>
public interface IBusinessImageStore
{
    Task<BusinessImage?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(BusinessImage image);

    /// <summary>Removes an image. Silent when the id is unknown.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
