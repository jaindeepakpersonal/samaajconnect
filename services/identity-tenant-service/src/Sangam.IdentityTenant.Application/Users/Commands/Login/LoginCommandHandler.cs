using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.Login;

public sealed class LoginCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordHasher passwordHasher,
    IFailedLoginRecorder failedLoginRecorder,
    ITokenIssuer tokenIssuer,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    /// <summary>
    /// One message for "no such account" and for "wrong password". Telling the
    /// two apart hands an attacker a free account-enumeration oracle.
    /// </summary>
    private static Result<LoginResponse> InvalidCredentials() =>
        Result.Failure<LoginResponse>(
            Error.Unauthorized("Auth.InvalidCredentials", "Incorrect mobile/email or password."));

    public async Task<Result<LoginResponse>> Handle(
        LoginCommand command,
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
            // This does reveal that the account exists. The trade is deliberate:
            // a locked-out member who is told nothing will keep guessing and
            // extend their own lockout, and an attacker who has already
            // triggered a lockout has learned as much from the timing anyway.
            return Result.Failure<LoginResponse>(Error.Forbidden(
                "Auth.LockedOut",
                "Too many failed attempts. Try again in a few minutes."));
        }

        if (!passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            // Written through its own connection: this handler is about to
            // return a failure, and the transaction around it will roll back.
            await failedLoginRecorder.RecordAsync(user.Id, cancellationToken);

            return InvalidCredentials();
        }

        if (user.Status != UserStatus.Active)
        {
            return Result.Failure<LoginResponse>(
                Error.Forbidden("Auth.AccountSuspended", "This account has been suspended."));
        }

        // A Super Admin belongs to the platform, not to a Samaaj, so there is no
        // tenant to check the status of and no subdomain to redirect to.
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

        user.RecordSuccessfulLogin(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var token = tokenIssuer.Issue(
            user.Id, user.TenantId, user.MobileOrEmail, authorization.Roles, authorization.Permissions);

        return Result.Success(new LoginResponse(
            token.Token,
            token.ExpiresAt,
            user.Id,
            user.TenantId,
            tenant?.Slug ?? string.Empty,
            user.FullName,
            authorization.Roles));
    }
}
