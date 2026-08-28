using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.ListTenants;

/// <summary>
/// Every Samaaj on the platform, in every status. The Super Admin's tenant list.
/// </summary>
/// <remarks>
/// Deliberately separate from <c>ListRegisterableTenantsQuery</c>, which is
/// anonymous and returns only active Samaaj as public summaries. This one
/// returns the full record - contact details, enabled modules, archived
/// Samaaj - so the two must not be one endpoint with a flag: a flag is one
/// forgotten check away from handing an anonymous caller the whole table.
///
/// <paramref name="Status"/> filters; null means every status, which is what
/// the screen wants by default because an Inactive Samaaj is exactly the one an
/// admin is looking for.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.TenantManage)]
public sealed record ListTenantsQuery(string? Status, string? Search)
    : IQuery<IReadOnlyList<TenantResponse>>;
