using FluentValidation;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.SignOut;

/// <summary>
/// Ends the session the given refresh token belongs to.
/// </summary>
/// <remarks>
/// Anonymous, and that is deliberate. Signing out is the one thing a member
/// must be able to do when something has gone wrong - an expired access token,
/// a device they want to abandon - and requiring a valid access token would
/// mean the moment you most want to end a session is the moment you cannot.
/// The refresh token is the credential, and presenting it only ever destroys
/// the presenter's own session.
///
/// <see cref="Everywhere"/> ends every session for the account rather than just
/// this one: "sign out on all my devices", and the thing to do when a member
/// thinks their password is known. It needs the account to be identified, which
/// the token does.
/// </remarks>
[AllowAnonymousRequest]
public sealed record SignOutCommand(string RefreshToken, bool Everywhere = false)
    : ICommand<SignOutResponse>;

/// <summary>
/// <paramref name="SessionsEnded"/> is zero when the token was unknown or the
/// session already over. That is reported as success: signing out twice should
/// look exactly like signing out once, and a count that distinguished them
/// would say which tokens exist.
/// </summary>
public sealed record SignOutResponse(int SessionsEnded);

public sealed class SignOutCommandValidator : AbstractValidator<SignOutCommand>
{
    public SignOutCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
    }
}
