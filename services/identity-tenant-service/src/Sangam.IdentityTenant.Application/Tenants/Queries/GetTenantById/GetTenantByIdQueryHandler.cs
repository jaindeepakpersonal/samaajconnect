using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.GetTenantById;

public sealed class GetTenantByIdQueryHandler(ITenantRepository tenants)
    : IRequestHandler<GetTenantByIdQuery, Result<TenantSummaryResponse>>
{
    public async Task<Result<TenantSummaryResponse>> Handle(
        GetTenantByIdQuery query,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(query.TenantId, cancellationToken);

        // Archived is reported as absent, as everywhere else this summary is
        // served: "this Samaaj existed once" is not something to hand out.
        if (tenant is null || tenant.Status == TenantStatus.Archived)
        {
            return Result.Failure<TenantSummaryResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj matches that id."));
        }

        return Result.Success(tenant.ToSummaryResponse());
    }
}
