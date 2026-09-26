namespace Sangam.IdentityTenant.Domain.Authorization;

/// <summary>
/// A role grant. <see cref="TenantScope"/> is null for a platform-wide grant
/// ("all" in DATA-MODEL.md section 2), which is how Super Admin is represented
/// — there is no separate super-admin flag to forget to check.
/// </summary>
public sealed class UserRole
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public Guid? TenantScope { get; private set; }

    /// <summary>
    /// Set only on an <see cref="AuthorizationCatalog.RoleIds.AreaAdmin"/> grant,
    /// to the one locality that grant is restricted to. Null on every other
    /// role's grant, and null on an AreaAdmin grant would mean an
    /// area admin with no area - so callers that mint an AreaAdmin grant must
    /// supply one; nothing here defaults it.
    /// </summary>
    public Guid? LocalityId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    private UserRole() { }

    public UserRole(
        Guid userId, Guid roleId, Guid? tenantScope, Guid? localityId, DateTimeOffset assignedAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        RoleId = roleId;
        TenantScope = tenantScope;
        LocalityId = localityId;
        AssignedAt = assignedAt;
    }

    public bool AppliesTo(Guid tenantId) => TenantScope is null || TenantScope == tenantId;
}
