using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.ChangeTenantStatus;

public sealed class ChangeTenantStatusCommandHandler(
    ITenantRepository tenants,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
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
            return Result.Failure<TenantResponse>(Error.Conflict(
                "Tenant.Archived", "An archived Samaaj cannot be reactivated."));
        }

        // A no-op change returns success without raising an event, so the audit
        // log records decisions rather than repeated clicks.
        tenant.ChangeStatus(status, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(tenant.ToResponse());
    }
}
