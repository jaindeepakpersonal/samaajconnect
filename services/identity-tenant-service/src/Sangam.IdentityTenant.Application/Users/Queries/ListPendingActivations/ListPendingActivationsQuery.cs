using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Queries.ListPendingActivations;

/// <summary>
/// Accounts in this Samaaj waiting to be activated - the admin's list of people
/// still owed a code.
/// </summary>
[RequiresRoles(Roles.SamaajAdmin, Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.AdminUsersManage)]
public sealed record ListPendingActivationsQuery : IQuery<IReadOnlyList<PendingActivationResponse>>;

public sealed record PendingActivationResponse(
    Guid UserId,
    string FullName,
    string MobileOrEmail,
    DateTimeOffset CreatedAt,
    bool HasUsableCode,
    DateTimeOffset? CodeExpiresAt);
