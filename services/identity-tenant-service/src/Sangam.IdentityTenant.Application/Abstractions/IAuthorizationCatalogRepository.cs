using Sangam.IdentityTenant.Application.Authorization.Queries.ListRoles;
using Sangam.IdentityTenant.Domain.Authorization;

namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// The role matrix as one Samaaj sees it: the platform defaults, plus that
/// Samaaj's overrides.
/// </summary>
public interface IAuthorizationCatalogRepository
{
    /// <summary>
    /// The effective matrix for a Samaaj, or the bare platform defaults when
    /// <paramref name="tenantId"/> is null — which is what a Super Admin who has
    /// chosen no Samaaj is looking at.
    /// </summary>
    /// <remarks>
    /// <paramref name="callerMayEdit"/> is passed in rather than worked out here:
    /// it depends on the caller's permissions, which a repository has no business
    /// knowing. It matters that the flag means <b>this caller can edit</b> rather
    /// than <b>this matrix is editable</b> — an ordinary member may read the
    /// matrix, and a screen told the second would offer them controls the server
    /// refuses.
    /// </remarks>
    Task<RoleMatrixResponse> GetMatrixAsync(
        Guid? tenantId, bool callerMayEdit, CancellationToken cancellationToken = default);

    Task<RolePermissionOverride?> FindOverrideAsync(
        Guid tenantId, Guid roleId, Guid permissionId, CancellationToken cancellationToken = default);

    /// <summary>Every override a Samaaj holds, for resolving a user's permissions.</summary>
    Task<IReadOnlyList<RolePermissionOverride>> ListOverridesAsync(
        Guid tenantId, CancellationToken cancellationToken = default);

    void AddOverride(RolePermissionOverride entry);

    void RemoveOverride(RolePermissionOverride entry);
}
