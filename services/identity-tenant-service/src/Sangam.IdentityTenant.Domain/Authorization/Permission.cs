namespace Sangam.IdentityTenant.Domain.Authorization;

/// <summary>
/// Platform-level seeded reference data. Keys follow {Module}.{Action}
/// (SECURITY-CHECKLIST.md).
/// </summary>
public sealed class Permission
{
    public Guid Id { get; private set; }

    public string Key { get; private set; } = null!;

    private Permission() { }

    public Permission(Guid id, string key)
    {
        Id = id;
        Key = key;
    }
}
