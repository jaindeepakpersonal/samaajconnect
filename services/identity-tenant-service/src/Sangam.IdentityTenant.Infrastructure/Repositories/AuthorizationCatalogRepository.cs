using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Authorization.Queries.ListRoles;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Repositories;

/// <summary>
/// Reads the matrix as the platform defaults plus one Samaaj's overrides.
/// </summary>
/// <remarks>
/// The defaults come from <see cref="AuthorizationCatalog"/> in memory rather
/// than from the seeded tables, which is the same choice
/// <c>ListRolesQueryHandler</c> made and for the same reason: the catalogue is
/// what the pipeline behaviour actually checks against, so reading it means this
/// cannot report a matrix that has drifted from the one being enforced. The
/// overrides are the only part that lives in the database, because they are the
/// only part a Samaaj can change.
/// </remarks>
public sealed class AuthorizationCatalogRepository(IdentityTenantDbContext dbContext)
    : IAuthorizationCatalogRepository
{
    public async Task<RoleMatrixResponse> GetMatrixAsync(
        Guid? tenantId, bool callerMayEdit, CancellationToken cancellationToken = default)
    {
        var overrides = tenantId is { } id && id != Guid.Empty
            ? await ListOverridesAsync(id, cancellationToken)
            : [];

        var permissionsById = AuthorizationCatalog.Permissions.ToDictionary(p => p.Id, p => p.Key);

        var roles = AuthorizationCatalog.Roles
            .Select(role => new RoleResponse(
                role.Id,
                role.Name,
                AuthorizationCatalog.IsAdminAssignable(role.Id),
                [.. EffectivePermissionIds(role.Id, overrides)
                    .Select(permissionId => permissionsById[permissionId])
                    .OrderBy(key => key, StringComparer.Ordinal)],
                MatrixEditing.IsEditable(role.Id)))
            .ToList();

        return new RoleMatrixResponse(
            [.. AuthorizationCatalog.Permissions.Select(p => p.Key)],
            roles,

            // Both halves. A Samaaj has to be in scope for an override to have
            // anywhere to go, and the caller has to hold Roles.Manage - an
            // ordinary member may read this matrix, and a screen told it was
            // editable would offer them controls the server refuses.
            Editable: callerMayEdit && tenantId is { } scoped && scoped != Guid.Empty,
            EditableNote(tenantId));
    }

    public Task<RolePermissionOverride?> FindOverrideAsync(
        Guid tenantId, Guid roleId, Guid permissionId, CancellationToken cancellationToken = default) =>
        dbContext.RolePermissionOverrides
            .FirstOrDefaultAsync(
                o => o.TenantId == tenantId && o.RoleId == roleId && o.PermissionId == permissionId,
                cancellationToken);

    public async Task<IReadOnlyList<RolePermissionOverride>> ListOverridesAsync(
        Guid tenantId, CancellationToken cancellationToken = default) =>
        await dbContext.RolePermissionOverrides

            // Read on paths that have no resolved tenant - the login handler
            // resolves a user's permissions before any request context exists -
            // so the filter is ignored and the tenant applied by hand. Every
            // caller passes the tenant it means.
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId)
            .ToListAsync(cancellationToken);

    public void AddOverride(RolePermissionOverride entry) =>
        dbContext.RolePermissionOverrides.Add(entry);

    public void RemoveOverride(RolePermissionOverride entry) =>
        dbContext.RolePermissionOverrides.Remove(entry);

    /// <summary>
    /// The permissions a role carries in this Samaaj: the defaults, minus what
    /// has been revoked, plus what has been added.
    /// </summary>
    public static IEnumerable<Guid> EffectivePermissionIds(
        Guid roleId, IReadOnlyList<RolePermissionOverride> overrides)
    {
        var effective = AuthorizationCatalog.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionId)
            .ToHashSet();

        foreach (var entry in overrides.Where(o => o.RoleId == roleId))
        {
            if (entry.Granted)
            {
                effective.Add(entry.PermissionId);
            }
            else
            {
                effective.Remove(entry.PermissionId);
            }
        }

        return effective;
    }

    private static string EditableNote(Guid? tenantId) =>
        tenantId is { } id && id != Guid.Empty
            ? "Changes apply to this Samaaj only. The platform defaults are untouched, so a "
              + "permission set back to its default resumes tracking that default as it changes. "
              + "SuperAdmin cannot be edited, and a Samaaj administrator cannot be stripped of "
              + "Roles.Manage - that is the one change a Samaaj could not undo for itself."
            : "These are the platform defaults. Choose a Samaaj to see and change what its "
              + "roles may do.";
}
