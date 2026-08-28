using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.GetTenantBySlug;

/// <summary>
/// Resolves a subdomain slug to a tenant. Anonymous by necessity: the gateway
/// calls this before any JWT exists, to decide which tenant a request belongs
/// to at all (ARCHITECTURE.md §6).
/// </summary>
[AllowAnonymousRequest]
public sealed record GetTenantBySlugQuery(string Slug) : IQuery<TenantSummaryResponse>;
