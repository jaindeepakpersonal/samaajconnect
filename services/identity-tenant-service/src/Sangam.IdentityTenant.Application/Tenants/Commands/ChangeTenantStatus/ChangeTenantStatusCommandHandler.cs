using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.ChangeTenantStatus;

public sealed class ChangeTenantStatusCommandHandler(
    ITenantRepository tenants,
    IStepUpAuthentication stepUp,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ICurrentUser currentUser,
    ILogger<ChangeTenantStatusCommandHandler> logger)
    : IRequestHandler<ChangeTenantStatusCommand, Result<TenantResponse>>
{
    public async Task<Result<TenantResponse>> Handle(
        ChangeTenantStatusCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure<TenantResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        var status = Enum.Parse<TenantStatus>(command.Status, ignoreCase: true);

        if (tenant.Status == TenantStatus.Archived && status != TenantStatus.Archived)
        {
            // One-way on purpose. Un-archiving would resurrect a Samaaj whose
            // members and data other services may already have stopped
            // maintaining; reinstating one should be a deliberate, manual event.
            //
            // Checked before the step-up so a request that cannot succeed says
            // so without first demanding a password. Nothing is given away by
            // the order: the caller is a Super Admin who can already read this
            // Samaaj's status.
            return Result.Failure<TenantResponse>(Error.Conflict(
                "Tenant.Archived", "An archived Samaaj cannot be reactivated."));
        }

        if (ChangeTenantStatusCommand.RequiresStepUp(status))
        {
            var confirmed = await stepUp.ConfirmAsync(
                command.Password,
                status == TenantStatus.Archived
                    ? "Archiving a Samaaj"
                    : "Taking a Samaaj out of service",
                cancellationToken);

            if (confirmed.IsFailure)
            {
                // Worth a line in its own right. A failed step-up on a
                // destructive administrative action is either somebody
                // mistyping or somebody at a machine that is not theirs, and
                // only the pattern over time tells those apart.
                logger.LogWarning(
                    "Step-up refused for {Actor} changing Samaaj {TenantId} to {Status}",
                    currentUser.UserId,
                    tenant.Id,
                    status);

                return Result.Failure<TenantResponse>(confirmed.Error);
            }
        }

        // A no-op change returns success without raising an event, so the audit
        // log records decisions rather than repeated clicks.
        tenant.ChangeStatus(status, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(tenant.ToResponse());
    }
}
