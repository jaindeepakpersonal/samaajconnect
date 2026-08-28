using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.Login;

/// <summary>
/// Common login: the member types one identifier and lands in their own
/// Samaaj. No tenant is supplied, because the identifier is unique
/// platform-wide and therefore already names exactly one Samaaj
/// (member-portal wireframe).
/// </summary>
[AllowAnonymousRequest]
public sealed record LoginCommand(string MobileOrEmail, string Password) : ICommand<LoginResponse>;
