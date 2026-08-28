using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.ActivateAccount;

public sealed class ActivateAccountCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordHasher passwordHasher,
    IFailedActivationRecorder failedActivations,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<ActivateAccountCommandHandler> logger)
    : IRequestHandler<ActivateAccountCommand, Result<ActivateAccountResponse>>
{
    /// <summary>
    /// One message for every way this can fail. Distinguishing "no such
    /// account", "already activated" and "wrong code" would let someone with a
    /// list of identifiers work out which ones are mid-conversion.
    /// </summary>
    private static Result<ActivateAccountResponse> InvalidActivation() =>
        Result.Failure<ActivateAccountResponse>(Error.Forbidden(
            "Activation.Invalid",
            "That activation code is not valid. Ask your Samaaj administrator for a new one."));

    public async Task<Result<ActivateAccountResponse>> Handle(
        ActivateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var identifier = User.NormalizeIdentifier(command.MobileOrEmail);

        var user = await users.FindPendingActivationAsync(identifier, cancellationToken);

        if (user?.ActivationCode is not { } code)
        {
            return InvalidActivation();
        }

        if (!code.IsUsable(now))
        {
            return InvalidActivation();
        }

        if (!passwordHasher.Verify(command.Code, code.Hash))
        {
            // Written through its own connection: this handler returns a
            // failure, and TransactionBehavior rolls that back - so a counter
            // on the tracked aggregate would vanish and the code would be
            // guessable forever. Same shape as the login lockout.
            await failedActivations.RecordAsync(user.Id, cancellationToken);

            return InvalidActivation();
        }

        var tenant = await tenants.GetByIdAsync(user.TenantId, cancellationToken);

        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            return Result.Failure<ActivateAccountResponse>(Error.Forbidden(
                "Auth.SamaajUnavailable",
                "Your Samaaj is not currently active. Please contact your Samaaj administrator."));
        }

        user.Activate(passwordHasher.Hash(command.Password), now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Account {UserId} activated from a child conversion", user.Id);

        return Result.Success(new ActivateAccountResponse(user.Id, tenant.Slug, user.FullName));
    }
}
