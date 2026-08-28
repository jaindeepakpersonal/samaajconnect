using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.ListRegisterableTenants;

/// <summary>
/// The Samaaj a visitor may register into: active ones only.
/// </summary>
/// <remarks>
/// Anonymous by necessity - the member-portal registration form asks people to
/// pick their Samaaj before they have an account, so something has to fill that
/// list. Deliberately separate from the Super Admin's ListTenantsQuery, which
/// returns every Samaaj in every status with contact details attached: this one
/// returns the same public summary as slug resolution, so an anonymous caller
/// learns nothing here they could not learn by guessing a subdomain.
/// </remarks>
[AllowAnonymousRequest]
public sealed record ListRegisterableTenantsQuery : IQuery<IReadOnlyList<TenantSummaryResponse>>;
