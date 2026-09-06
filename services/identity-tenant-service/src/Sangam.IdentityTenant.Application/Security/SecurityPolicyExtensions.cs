using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;

namespace Sangam.IdentityTenant.Application.Security;

/// <summary>
/// The one place a resolved policy's rules turn into a Result failure, shared
/// by every command that sets a password (register, activate, change, reset)
/// rather than repeated in each.
/// </summary>
public static class SecurityPolicyExtensions
{
    /// <summary>
    /// Null when the password satisfies this Samaaj's minimum length. The
    /// platform-wide floor of 10 is already enforced by each command's own
    /// FluentValidation rule before this ever runs - this only ever asks for
    /// *more* than that, never less.
    /// </summary>
    public static Error? ValidatePasswordLength(this ResolvedSecurityPolicy policy, string password) =>
        password.Length < policy.MinPasswordLength
            ? Error.Validation(new Dictionary<string, string[]>
            {
                ["NewPassword"] =
                    [$"This Samaaj requires passwords of at least {policy.MinPasswordLength} characters."],
            })
            : null;
}
