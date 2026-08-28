using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.ListTenants;

public sealed class ListTenantsQueryHandler(ITenantRepository tenants)
    : IRequestHandler<ListTenantsQuery, Result<IReadOnlyList<TenantResponse>>>
{
    public async Task<Result<IReadOnlyList<TenantResponse>>> Handle(
        ListTenantsQuery query,
        CancellationToken cancellationToken)
    {
        TenantStatus? status = string.IsNullOrWhiteSpace(query.Status)
            ? null
            : Enum.Parse<TenantStatus>(query.Status, ignoreCase: true);

        var results = await tenants.ListAsync(status, query.Search, cancellationToken);

        IReadOnlyList<TenantResponse> responses = [.. results.Select(t => t.ToResponse())];

        return Result.Success(responses);
    }
}
