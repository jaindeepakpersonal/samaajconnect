using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.InviteAdmin;

/// <summary>
/// Creates an account for a new administrator in the caller's Samaaj and issues
/// their activation code in one step.
/// </summary>
/// <remarks>
/// The wireframe calls this "Send Invite" and says invited admins "receive a
/// set-password link". There is no notification channel yet, so what actually
/// happens is what already happens for a converted child: the account is
/// created unsignable-into, a one-time code is returned to the inviting admin
/// exactly once, and it is handed over in person. For a community organisation
/// whose administrators know each other, that is realistic, and it involves no
/// channel that can be intercepted. Replace the hand-over, not the flow, when a
/// delivery channel lands.
///
/// Creation and code issue are one command rather than two calls, because an
/// invitation that created the account and then failed to issue the code would
/// leave an account nobody can reach and no obvious way to tell that is what
/// happened. One command, one transaction.
///
/// The Samaaj is the caller's own, never a parameter.
/// </remarks>
// Security.Roles, fully qualified: this record has its own Roles property, and
// inside its own attribute list that name wins. An unqualified Roles.SamaajAdmin
// here does not compile, which is the good outcome - it could as easily have
// bound to something that did.
[RequiresRoles(Security.Roles.SuperAdmin, Security.Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.AdminUsersManage)]
public sealed record InviteAdminCommand(
    string FullName,
    string MobileOrEmail,
    IReadOnlyList<string> Roles) : ICommand<InviteAdminResponse>;

/// <summary>
/// <paramref name="ActivationCode"/> is plaintext and is returned exactly once;
/// only its hash is stored. There is no way to look it up again - a lost code
/// is re-issued, which kills the previous one.
/// </summary>
public sealed record InviteAdminResponse(
    Guid UserId,
    string FullName,
    string MobileOrEmail,
    IReadOnlyCollection<string> Roles,
    string ActivationCode,
    DateTimeOffset CodeExpiresAt);
