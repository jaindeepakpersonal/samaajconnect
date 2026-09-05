using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.SetUserSuspension;

public sealed class SetUserSuspensionCommandHandler(
    IUserRepository users,
    IStepUpAuthentication stepUp,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<SetUserSuspensionCommandHandler> logger)
    : IRequestHandler<SetUserSuspensionCommand, Result<UserStatusResponse>>
{
    public async Task<Result<UserStatusResponse>> Handle(
        SetUserSuspensionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<UserStatusResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserStatusResponse>(
                Error.NotFound("User.NotFound", "No account with that id exists in this Samaaj."));
        }

        // The IDOR guard (CLAUDE.md §6). This also excludes a SuperAdmin as a
        // target with no separate check: their account lives at
        // User.PlatformTenantId, which never equals a resolved Samaaj's id, so
        // GetByIdAsync's tenant filter already finds nothing for one - the same
        // way it does for every other write here.
        if (user.TenantId != tenantContext.TenantId)
        {
            return Result.Failure<UserStatusResponse>(
                Error.NotFound("User.NotFound", "No account with that id exists in this Samaaj."));
        }

        if (user.Status == UserStatus.Erased)
        {
            return Result.Failure<UserStatusResponse>(Error.Conflict(
                "User.Erased", "This account has been erased and cannot be suspended or reinstated."));
        }

        // Suspending yourself ends your own session and, unlike reinstating,
        // cannot be undone by the account it happened to - the same reasoning
        // AssignRoleCommandHandler gives for refusing a self-revoked last
        // Samaaj Admin role: it takes two people rather than one mis-click.
        if (command.Suspended && user.Id == actorId)
        {
            return Result.Failure<UserStatusResponse>(Error.Conflict(
                "User.SelfSuspend",
                "You cannot suspend your own account. Ask another administrator."));
        }

        if (SetUserSuspensionCommand.RequiresStepUp(command.Suspended))
        {
            var confirmed = await stepUp.ConfirmAsync(
                command.Password, "Suspending an account", cancellationToken);

            if (confirmed.IsFailure)
            {
                logger.LogWarning(
                    "Step-up refused for {Actor} suspending {UserId}", actorId, user.Id);

                return Result.Failure<UserStatusResponse>(confirmed.Error);
            }
        }

        var now = clock.UtcNow;

        var changed = command.Suspended
            ? user.Suspend(actorId, now)
            : user.Reinstate(actorId, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (changed)
        {
            logger.LogInformation(
                "{Action} account {UserId} in Samaaj {TenantId}, by {Actor}",
                command.Suspended ? "Suspended" : "Reinstated",
                user.Id,
                tenantContext.TenantId,
                actorId);
        }

        return Result.Success(new UserStatusResponse(user.Id, user.Status.ToString(), changed));
    }
}
