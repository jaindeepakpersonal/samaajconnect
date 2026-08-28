using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.ListRegisterableTenants;

public sealed class ListRegisterableTenantsQueryHandler(ITenantRepository tenants)
    : IRequestHandler<ListRegisterableTenantsQuery, Result<IReadOnlyList<TenantSummaryResponse>>>
{
    public async Task<Result<IReadOnlyList<TenantSummaryResponse>>> Handle(
        ListRegisterableTenantsQuery query,
        CancellationToken cancellationToken)
    {
        var active = await tenants.ListActiveAsync(cancellationToken);

        IReadOnlyList<TenantSummaryResponse> results =
            active.Select(tenant => tenant.ToSummaryResponse()).ToList();

        return Result.Success(results);
    }
}
