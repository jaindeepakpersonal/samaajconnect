using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.SetTenantModules;

/// <summary>
/// Replaces the set of modules a Samaaj runs.
/// </summary>
/// <remarks>
/// Super Admin only, alongside the other tenant-shape commands. Which modules a
/// Samaaj runs is a platform-level decision - it is what the gateway routes on,
/// and switching one off makes a whole area of the platform answer 404 for
/// everybody in that Samaaj. A Samaaj Admin manages people and content inside
/// the modules they have; they do not decide which ones exist.
///
/// The whole set is submitted, not a delta, matching both the screen (a row of
/// toggles saved together) and the aggregate.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.TenantManage)]
public sealed record SetTenantModulesCommand(Guid TenantId, IReadOnlyList<string> EnabledModules)
    : ICommand<TenantResponse>;
