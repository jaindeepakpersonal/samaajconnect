using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.LoginWithOtp;

/// <summary>
/// Signs in with a one-time code instead of a password. Returns the same
/// <see cref="LoginResponse"/> as <see cref="Login.LoginCommand"/> - this is
/// an alternate way to get the same thing, not a different one.
/// </summary>
[AllowAnonymousRequest]
public sealed record LoginWithOtpCommand(string MobileOrEmail, string Code) : ICommand<LoginResponse>;
