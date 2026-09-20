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

public interface IStateRepository
{
    Task<State?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<State>> ListAsync(
        Guid? countryId, bool includeInactive, CancellationToken cancellationToken = default);

    void Add(State state);
}

public interface ICityRepository
{
    Task<City?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<City>> ListAsync(
        Guid? countryId, Guid? stateId, bool includeInactive, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Whether this Samaaj already has a Gautra by this exact name.
    /// Tenant-filtered, mirroring the unique index on (TenantId, Name) - checked
    /// here so a duplicate is a clean 409 rather than an unhandled database
    /// exception surfacing as a 500, the same reason
    /// <c>ILookupItemRepository.CodeExistsAsync</c> exists.
    /// </summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Gautra>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken = default);

    void Add(Gautra gautra);
}
