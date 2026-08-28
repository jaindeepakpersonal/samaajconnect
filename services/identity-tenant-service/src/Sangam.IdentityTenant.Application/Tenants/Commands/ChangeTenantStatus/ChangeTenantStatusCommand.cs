using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.ChangeTenantStatus;

/// <summary>
/// Activates, deactivates or archives a Samaaj. Separate from creation because
/// letting a Samaaj serve traffic is its own decision with its own audit trail.
/// </summary>
[RequiresRoles(Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.TenantManage)]
public sealed record ChangeTenantStatusCommand(Guid TenantId, string Status) : ICommand<TenantResponse>;
