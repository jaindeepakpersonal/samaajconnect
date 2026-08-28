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

    public DateTimeOffset AssignedAt { get; private set; }

    private UserRole() { }

    public UserRole(Guid userId, Guid roleId, Guid? tenantScope, DateTimeOffset assignedAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        RoleId = roleId;
        TenantScope = tenantScope;
        AssignedAt = assignedAt;
    }

    public bool AppliesTo(Guid tenantId) => TenantScope is null || TenantScope == tenantId;
}
