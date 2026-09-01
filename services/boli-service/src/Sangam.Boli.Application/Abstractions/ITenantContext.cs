namespace Sangam.Boli.Application.Abstractions;

/// <summary>
/// Request-scoped tenant, populated from the X-Tenant-Id header the gateway
/// injects after resolving the subdomain. A Super Admin's
/// X-Tenant-Override-Id populates this same property — there is no separate
/// admin bypass path in any service (CLAUDE.md §6).
/// </summary>
public interface ITenantContext
{
    /// <summary>Null on genuinely tenant-less requests, e.g. resolving a slug or creating a tenant.</summary>
    Guid? TenantId { get; }

    /// <summary>True when this request's tenant came from a Super Admin override header.</summary>
    bool IsOverride { get; }

    /// <summary>
    /// True when the request carried a tenant header that disagrees with the
    /// tenant its token was issued for. A caller holding a valid token for one
    /// Samaaj must not be able to point it at another simply by setting a
    /// header, so the pipeline refuses such a request outright.
    /// </summary>
    bool HasTenantConflict { get; }

    /// <summary>
    /// Throws when a tenant-scoped operation runs without a resolved tenant.
    /// Use in handlers that must never fall back to "all tenants".
    /// </summary>
    Guid RequireTenantId();
}
