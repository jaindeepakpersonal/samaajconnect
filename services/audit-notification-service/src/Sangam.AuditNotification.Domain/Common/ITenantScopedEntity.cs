namespace Sangam.AuditNotification.Domain.Common;

/// <summary>
/// Marks an entity as owned by exactly one tenant. The DbContext reflects over
/// implementers of this interface to apply the global query filter, so an
/// entity gets tenant isolation on reads purely by implementing it
/// (CLAUDE.md §6).
/// </summary>
/// <remarks>
/// <see cref="Tenants.Tenant"/> deliberately does <b>not</b> implement this:
/// it is a platform-level entity whose own Id <i>is</i> the tenant id, and
/// filtering it by tenant would make slug resolution impossible.
/// </remarks>
public interface ITenantScopedEntity
{
    Guid TenantId { get; }
}
