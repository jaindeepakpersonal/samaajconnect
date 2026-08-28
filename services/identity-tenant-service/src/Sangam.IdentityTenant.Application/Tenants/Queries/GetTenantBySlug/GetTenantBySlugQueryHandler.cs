using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.GetTenantBySlug;

public sealed class GetTenantBySlugQueryHandler(ITenantRepository tenants)
    : IRequestHandler<GetTenantBySlugQuery, Result<TenantSummaryResponse>>
{
    public async Task<Result<TenantSummaryResponse>> Handle(
        GetTenantBySlugQuery query,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetBySlugAsync(
            Tenant.NormalizeSlug(query.Slug), cancellationToken);

        // An archived Samaaj is treated as absent rather than as a distinct
        // state: this response is public, and "this slug existed once" is not
        // something an anonymous caller needs to be able to discover.
        if (tenant is null || tenant.Status == TenantStatus.Archived)
        {
            return Result.Failure<TenantSummaryResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj matches that address."));
        }

        return Result.Success(tenant.ToSummaryResponse());
    }
}
