namespace Sangam.IdentityTenant.Domain.Authorization;

/// <summary>
/// Platform-level seeded reference data (DATA-MODEL.md section 2). Roles are
/// the same nine everywhere; what varies per Samaaj is who holds them.
/// </summary>
public sealed class Role
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    private Role() { }

    public Role(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
}
