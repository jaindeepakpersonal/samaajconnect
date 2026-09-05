using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.RequestLoginOtp;

/// <summary>
/// Asks for a one-time sign-in code. Answers the same way whether or not the
/// identifier belongs to a real, active account - telling the two apart would
/// hand an attacker a free account-enumeration oracle, exactly the reason
/// <see cref="Login.LoginCommandHandler"/> gives one message for "no such
/// account" and "wrong password".
/// </summary>
[AllowAnonymousRequest]
public sealed record RequestLoginOtpCommand(string MobileOrEmail) : ICommand<RequestLoginOtpResponse>;

public sealed record RequestLoginOtpResponse;
