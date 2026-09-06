using Sangam.MasterData.Domain.Gautras;
using Sangam.MasterData.Domain.Locations;
using Sangam.MasterData.Domain.Lookups;

namespace Sangam.MasterData.Application.Abstractions;

public interface ILookupItemRepository
{
    Task<LookupItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(
        LookupCategory category, string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupItem>> ListAsync(
        LookupCategory category, bool includeInactive, CancellationToken cancellationToken = default);

    void Add(LookupItem item);
}

public interface ICityRepository
{
    Task<City?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<City>> ListAsync(
        Guid? countryId, bool includeInactive, CancellationToken cancellationToken = default);

    void Add(City city);
}

public interface ILocalityRepository
{
    Task<Locality?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Locality>> ListAsync(
        Guid? cityId, bool includeInactive, CancellationToken cancellationToken = default);

    void Add(Locality locality);
}

public interface IGautraRepository
{
    /// <summary>Tenant-filtered by the DbContext's global query filter.</summary>
    Task<Gautra?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Gautra>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken = default);

    void Add(Gautra gautra);
}
