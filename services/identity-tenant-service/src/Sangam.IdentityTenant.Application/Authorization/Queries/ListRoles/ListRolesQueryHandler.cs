using MediatR;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Authorization;

namespace Sangam.IdentityTenant.Application.Authorization.Queries.ListRoles;

public sealed class ListRolesQueryHandler
    : IRequestHandler<ListRolesQuery, Result<RoleMatrixResponse>>
{
    private const string EditableNote =
        "This matrix is what the backend enforces, and it is fixed in the platform's "
        + "source. Editing it at runtime would split the answer to \"who may do this?\" "
        + "between code and a table, so the screen shows it rather than edits it.";

    public Task<Result<RoleMatrixResponse>> Handle(
        ListRolesQuery query,
        CancellationToken cancellationToken)
    {
        // Straight from the catalogue, not from the database. The two are
        // identical - the catalogue is what the seed migration writes - and
        // reading the source of truth means this endpoint cannot report a
        // matrix that drifted from the one the pipeline behaviour checks.
        var permissionsById = AuthorizationCatalog.Permissions.ToDictionary(p => p.Id, p => p.Key);

        var roles = AuthorizationCatalog.Roles
            .Select(role => new RoleResponse(
                role.Id,
                role.Name,
                AuthorizationCatalog.IsAdminAssignable(role.Id),
                [.. AuthorizationCatalog.RolePermissions
                    .Where(rp => rp.RoleId == role.Id)
                    .Select(rp => permissionsById[rp.PermissionId])
                    .OrderBy(key => key, StringComparer.Ordinal)]))
            .ToList();

        var response = new RoleMatrixResponse(
            [.. AuthorizationCatalog.Permissions.Select(p => p.Key)],
            roles,
            Editable: false,
            EditableNote);

        return Task.FromResult(Result.Success(response));
    }
}
