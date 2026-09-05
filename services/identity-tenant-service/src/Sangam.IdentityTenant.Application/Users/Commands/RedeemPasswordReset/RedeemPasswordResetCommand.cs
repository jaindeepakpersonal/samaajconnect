using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.RedeemPasswordReset;

/// <summary>
/// Redeems a password reset code and sets a new password. No token: proving
/// contact-address access is weaker than a real password, so the next step is
/// an ordinary sign-in, not an automatic one - the same choice
/// <see cref="ActivateAccount.ActivateAccountCommand"/> makes for the same
/// reason.
/// </summary>
[AllowAnonymousRequest]
public sealed record RedeemPasswordResetCommand(
    string MobileOrEmail, string Code, string NewPassword) : ICommand<RedeemPasswordResetResponse>;

public sealed record RedeemPasswordResetResponse(Guid UserId);
