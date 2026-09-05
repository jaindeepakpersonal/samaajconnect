using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.RedeemPasswordReset;

/// <summary>
/// The verification half mirrors
/// <see cref="LoginWithOtp.LoginWithOtpCommandHandler"/> - same recorder,
/// same lockout, same one indistinguishable failure. No tenant or account
/// status check beyond that: unlike signing in, this does not need the
/// account to be reachable right now, only for a valid code to exist against
/// it.
/// </summary>
public sealed class RedeemPasswordResetCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IFailedLoginRecorder failedLoginRecorder,
    ISessionService sessions,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<RedeemPasswordResetCommand, Result<RedeemPasswordResetResponse>>
{
    private static Result<RedeemPasswordResetResponse> InvalidCode() =>
        Result.Failure<RedeemPasswordResetResponse>(
            Error.Unauthorized("Auth.InvalidCredentials", "That code is not valid."));

    public async Task<Result<RedeemPasswordResetResponse>> Handle(
        RedeemPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var identifier = User.NormalizeIdentifier(command.MobileOrEmail);

        var user = await users.FindForLoginAsync(identifier, cancellationToken);

        if (user is null)
        {
            return InvalidCode();
        }

        if (user.IsLockedOut(now))
        {
            return Result.Failure<RedeemPasswordResetResponse>(Error.Forbidden(
                "Auth.LockedOut", "Too many failed attempts. Try again in a few minutes."));
        }

        if (user.PasswordResetCode is not { } code
            || !code.IsUsable(now)
            || !passwordHasher.Verify(command.Code, code.Hash))
        {
            await failedLoginRecorder.RecordAsync(user.Id, cancellationToken);

            return InvalidCode();
        }

        user.ResetPassword(passwordHasher.Hash(command.NewPassword), now);

        // Every session ends here, the same as an authenticated change -
        // a stolen refresh token is worth nothing once the password it was
        // issued under no longer exists.
        await sessions.EndAllForUserAsync(user.Id, SessionEndReason.PasswordChanged, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RedeemPasswordResetResponse(user.Id));
    }
}
