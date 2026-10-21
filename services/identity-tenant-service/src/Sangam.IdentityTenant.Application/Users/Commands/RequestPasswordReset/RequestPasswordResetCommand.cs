using Sangam.IdentityTenant.Application.Abstractions;
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

public sealed record RequestPasswordResetResponse(
    /// <summary>
    /// The code itself, echoed back only when
    /// <see cref="SignInSecurityOptions.RevealSecretsInLogs"/> is on - the same
    /// local-compose-only case the sign-in code's own <c>DevCode</c> covers,
    /// reused here rather than a second flag for the same "no email/SMS
    /// provider" situation. Present or absent tracks exactly what the answer
    /// already always has: whether the identifier belongs to a real, active
    /// account. That is only acceptable because it is the same local stack
    /// whose log already reveals it the same way - see
    /// <c>RequestPasswordResetCommandHandler</c>.
    /// </summary>
    string? DevCode = null);
