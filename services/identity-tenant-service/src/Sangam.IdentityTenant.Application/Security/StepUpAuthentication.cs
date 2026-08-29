using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;

namespace Sangam.IdentityTenant.Application.Security;

/// <summary>
/// Re-asks the person at the keyboard for their password before an
/// irreversible action.
/// </summary>
/// <remarks>
/// A bearer token proves somebody signed in, at some point in the last fifteen
/// minutes, on some device. For most requests that is enough. For an action
/// that cannot be undone it is not: the token is equally valid on a laptop
/// somebody walked away from, and the whole point of a step-up is to establish
/// that the account holder is present <i>now</i>, deliberately.
///
/// It is one small service rather than a rule copied into each handler because
/// the two things that are easy to get wrong here are shared, and getting
/// either wrong is quiet:
///
/// <b>The account must be read past the tenant filter.</b> A Super Admin's own
/// account lives at <see cref="Domain.Users.User.PlatformTenantId"/>, not in the
/// Samaaj they are acting on, so <c>GetByIdAsync</c> - which is tenant-filtered
/// - finds nothing and the step-up fails for the one role that most needs it.
/// This is the same trap that made <c>/me</c> answer 404 for an overriding
/// Super Admin; <c>GetSelfAsync</c> is the deliberate bypass, and it is safe
/// because the id is the token's own subject rather than anything the caller
/// supplied.
///
/// <b>A wrong password is 403, not 401.</b> A 401 tells the portals'
/// interceptor the access token has expired: it renews the token and
/// <i>retries the original request</i>. On an ordinary read that is invisible
/// and useful; on "deactivate this Samaaj" or "erase my account" it means a
/// destructive command is sent a second time because somebody mistyped. 403 is
/// also the truer answer - the caller is authenticated, they simply have not
/// proven enough for this - and it carries no <c>WWW-Authenticate</c>
/// obligation, which a 401 does and this never had.
/// </remarks>
public interface IStepUpAuthentication
{
    /// <summary>
    /// Confirms <paramref name="password"/> belongs to the calling account.
    /// </summary>
    /// <returns>
    /// Success, or a <see cref="ErrorType.Forbidden"/> failure carrying
    /// <see cref="StepUpFailedCode"/>.
    /// </returns>
    Task<Result> ConfirmAsync(
        string? password, string action, CancellationToken cancellationToken = default);

    /// <summary>
    /// The error code a failed step-up carries, so the portals can tell "that
    /// password is wrong" apart from "you may not do this at all" and keep the
    /// member on the screen they are already on.
    /// </summary>
    const string StepUpFailedCode = "Auth.StepUpFailed";
}

public sealed class StepUpAuthentication(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser)
    : IStepUpAuthentication
{
    public async Task<Result> ConfirmAsync(
        string? password, string action, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        // Deliberately the same failure as a wrong password. Telling an
        // unauthenticated-looking caller that they simply forgot the field is
        // harmless; telling them the field was accepted and the password was
        // wrong is not.
        if (string.IsNullOrEmpty(password))
        {
            return Failed(action);
        }

        // GetSelfAsync, not GetByIdAsync. See the remarks on the interface.
        var user = await users.GetSelfAsync(userId, cancellationToken);

        if (user is null || !passwordHasher.Verify(password, user.PasswordHash))
        {
            return Failed(action);
        }

        return Result.Success();
    }

    private static Result Failed(string action) =>
        Result.Failure(Error.Forbidden(
            IStepUpAuthentication.StepUpFailedCode,
            $"That password is not correct. {action} cannot be undone, so we ask for it first."));
}
