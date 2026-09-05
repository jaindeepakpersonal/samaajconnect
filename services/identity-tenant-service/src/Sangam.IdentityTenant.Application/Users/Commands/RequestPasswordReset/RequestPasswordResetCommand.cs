using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.RequestPasswordReset;

/// <summary>
/// Asks for a password reset code. Answers the same way whether or not the
/// identifier belongs to a real, active account - the same reasoning
/// <see cref="RequestLoginOtp.RequestLoginOtpCommand"/> follows.
/// </summary>
[AllowAnonymousRequest]
public sealed record RequestPasswordResetCommand(string MobileOrEmail) : ICommand<RequestPasswordResetResponse>;

public sealed record RequestPasswordResetResponse;
