using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Abstractions;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    Task<bool> DomainExistsAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>Active Samaaj, by name, for the public registration picker.</summary>
    Task<IReadOnlyList<Tenant>> ListActiveAsync(CancellationToken cancellationToken = default);

    void Add(Tenant tenant);
}
