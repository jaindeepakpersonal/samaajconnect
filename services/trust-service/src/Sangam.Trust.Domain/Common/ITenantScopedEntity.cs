namespace Sangam.Trust.Domain.Common;

/// <summary>
/// Marks an entity as owned by exactly one tenant. The DbContext reflects over
/// implementers of this interface to apply the global query filter, so an
/// entity gets tenant isolation on reads purely by implementing it
/// (CLAUDE.md §6).
/// </summary>
public interface ITenantScopedEntity
{
    Guid TenantId { get; }
}
