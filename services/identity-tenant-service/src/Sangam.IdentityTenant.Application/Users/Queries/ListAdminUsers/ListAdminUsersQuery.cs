using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Queries.ListAdminUsers;

/// <summary>
/// Everyone in this Samaaj holding a role beyond ordinary membership - the
/// admin portal's "Admin Users & Roles" list.
/// </summary>
/// <remarks>
/// Scoped to the caller's Samaaj by the tenant query filter, so a Samaaj Admin
/// sees their own administrators and no one else's. A Super Admin needing
/// another Samaaj's list uses the gateway override, which is logged.
///
/// Platform accounts do not appear. A Super Admin's grant is scoped to no
/// tenant at all (`TenantScope` is null), so they are nobody's Samaaj
/// administrator - and listing them here would hand every Samaaj Admin on the
/// platform the identifiers of the accounts worth attacking.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.AdminUsersManage)]
public sealed record ListAdminUsersQuery : IQuery<IReadOnlyList<AdminUserResponse>>;

public sealed record AdminUserResponse(
    Guid UserId,
    string FullName,
    string MobileOrEmail,
    string Status,
    DateTimeOffset? LastLoginAt,
    IReadOnlyCollection<string> Roles);
