using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.ActivateAccount;

/// <summary>
/// Redeems an activation code and sets the account's first password.
/// </summary>
/// <remarks>
/// Anonymous by necessity: the person doing this has no way to sign in yet.
/// The code is what stands in for authentication, which is why it is one-time,
/// short-lived, and dies after five wrong guesses.
/// </remarks>
[AllowAnonymousRequest]
public sealed record ActivateAccountCommand(string MobileOrEmail, string Code, string Password)
    : ICommand<ActivateAccountResponse>;

public sealed record ActivateAccountResponse(Guid UserId, string TenantSlug, string FullName);
