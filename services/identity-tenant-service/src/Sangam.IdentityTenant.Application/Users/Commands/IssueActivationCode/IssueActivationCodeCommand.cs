using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.IssueActivationCode;

/// <summary>
/// Mints a one-time code for an account waiting to be activated, and returns it
/// to the admin exactly once.
/// </summary>
/// <remarks>
/// Issued on demand rather than at account creation, and returned rather than
/// stored, because there is no notification channel to send it through: the
/// admin reads it out or writes it down. Re-issuing is expected - codes expire
/// after a week and paper gets lost - and each issue invalidates the last.
/// </remarks>
[RequiresRoles(Roles.SamaajAdmin, Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.AdminUsersManage)]
public sealed record IssueActivationCodeCommand(Guid UserId) : ICommand<ActivationCodeResponse>;

/// <summary>
/// <paramref name="Code"/> appears in this response and nowhere else. It is
/// stored only as a hash, so a lost code is re-issued rather than looked up.
/// </summary>
public sealed record ActivationCodeResponse(
    Guid UserId,
    string MobileOrEmail,
    string FullName,
    string Code,
    DateTimeOffset ExpiresAt);
