using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.SetTenantModules;

public sealed class SetTenantModulesCommandHandler(
    ITenantRepository tenants,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<SetTenantModulesCommandHandler> logger)
    : IRequestHandler<SetTenantModulesCommand, Result<TenantResponse>>
{
    public async Task<Result<TenantResponse>> Handle(
        SetTenantModulesCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure<TenantResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        if (tenant.Status == TenantStatus.Archived)
        {
            // Nothing routes for an archived Samaaj anyway, so this would be a
            // change with no effect that still looked like one in the log.
            return Result.Failure<TenantResponse>(Error.Conflict(
                "Tenant.Archived", "An archived Samaaj's modules cannot be changed."));
        }

        if (tenant.SetEnabledModules(command.EnabledModules, clock.UtcNow))
        {
            logger.LogInformation(
                "Samaaj {TenantId} now runs modules: {Modules}",
                tenant.Id,
                string.Join(", ", tenant.EnabledModules));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(tenant.ToResponse());
    }
}
