namespace Sangam.IdentityTenant.Domain.Authorization;

public sealed class RolePermission
{
    public Guid RoleId { get; private set; }

    public Guid PermissionId { get; private set; }

    private RolePermission() { }

    public RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }
}
