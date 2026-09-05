using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.LoginWithOtp;

/// <summary>
/// <see cref="Login.LoginCommandHandler"/> with one substitution: where that
/// handler verifies a password, this verifies a code. Everything else - the
/// lockout, the one indistinguishable failure message, the account and Samaaj
/// status checks, issuing the session - is the same, on purpose. A member who
/// signed in with a code should not be able to tell, from the outside, that a
/// different check ran.
/// </summary>
public sealed class LoginWithOtpCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordHasher passwordHasher,
    IFailedLoginRecorder failedLoginRecorder,
    ITokenIssuer tokenIssuer,
    ISessionService sessions,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<LoginWithOtpCommand, Result<LoginResponse>>
{
    private static Result<LoginResponse> InvalidCredentials() =>
        Result.Failure<LoginResponse>(
            Error.Unauthorized("Auth.InvalidCredentials", "Incorrect mobile/email or password."));

    public async Task<Result<LoginResponse>> Handle(
        LoginWithOtpCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var identifier = User.NormalizeIdentifier(command.MobileOrEmail);

        var user = await users.FindForLoginAsync(identifier, cancellationToken);

        if (user is null)
        {
            return InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            return Result.Failure<LoginResponse>(Error.Forbidden(
                "Auth.LockedOut",
                "Too many failed attempts. Try again in a few minutes."));
        }

        if (user.LoginOtp is not { } otp || !otp.IsUsable(now) || !passwordHasher.Verify(command.Code, otp.Hash))
        {
            // Same recorder, same lockout, same message a wrong password
            // produces - one credential check with two ways to satisfy it,
            // not two checks with two failure modes to keep in step.
            await failedLoginRecorder.RecordAsync(user.Id, cancellationToken);

            return InvalidCredentials();
        }

        if (user.Status != UserStatus.Active)
        {
            return Result.Failure<LoginResponse>(
                Error.Forbidden("Auth.AccountSuspended", "This account has been suspended."));
        }

        Tenant? tenant = null;

        if (!user.IsPlatformAdministrator)
        {
            tenant = await tenants.GetByIdAsync(user.TenantId, cancellationToken);

            if (tenant is null || tenant.Status != TenantStatus.Active)
            {
                return Result.Failure<LoginResponse>(Error.Forbidden(
                    "Auth.SamaajUnavailable",
                    "Your Samaaj is not currently active. Please contact your Samaaj administrator."));
            }
        }

        var authorization = await users.GetAuthorizationAsync(user.Id, user.TenantId, cancellationToken);

        // Proves the member holds their own contact address, the same
        // assurance redeeming an activation code gives - so this is also
        // where IsContactVerified finally has a real way to become true.
        user.CompleteOtpSignIn();
        user.RecordSuccessfulLogin(now);

        var session = sessions.Begin(user.Id, user.TenantId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var token = tokenIssuer.Issue(
            user.Id, user.TenantId, user.MobileOrEmail, authorization.Roles, authorization.Permissions);

        return Result.Success(new LoginResponse(
            token.Token,
            token.ExpiresAt,
            session.RefreshToken,
            session.ExpiresAt,
            user.Id,
            user.TenantId,
            tenant?.Slug ?? string.Empty,
            user.FullName,
            authorization.Roles));
    }
}
