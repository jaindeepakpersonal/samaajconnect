using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;

namespace Sangam.IdentityTenant.Application.Authorization.Queries.ListRoles;

/// <summary>
/// The matrix as the calling Samaaj sees it.
/// </summary>
/// <remarks>
/// This used to read the platform catalogue directly and report
/// <c>editable: false</c>. It now asks the repository, because the answer
/// depends on which Samaaj is asking: the defaults are still the catalogue, but
/// a Samaaj that has changed something sees its own version.
///
/// A caller with no Samaaj in scope — a Super Admin who has not chosen one —
/// gets the bare defaults and <c>editable: false</c>, because there is nowhere
/// for an override to be written.
/// </remarks>
public sealed class ListRolesQueryHandler(
    IAuthorizationCatalogRepository catalog,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : IRequestHandler<ListRolesQuery, Result<RoleMatrixResponse>>
{
    public async Task<Result<RoleMatrixResponse>> Handle(
        ListRolesQuery query,
        CancellationToken cancellationToken) =>
        Result.Success(await catalog.GetMatrixAsync(
            tenantContext.TenantId,
            currentUser.HasPermission(Security.PermissionKeys.RolesManage),
            cancellationToken));
}
