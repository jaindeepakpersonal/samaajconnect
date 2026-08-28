using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.CreateTenant;

/// <summary>
/// Registers a new Samaaj on the platform. Super Admin only — this is the one
/// command in the platform that is not scoped to an existing tenant.
/// </summary>
[RequiresRoles(Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.TenantManage)]
public sealed record CreateTenantCommand(
    string Name,
    string Slug,
    string? Domain,
    string? ContactPerson,
    string? ContactEmail,
    IReadOnlyCollection<string>? EnabledModules) : ICommand<TenantResponse>;
