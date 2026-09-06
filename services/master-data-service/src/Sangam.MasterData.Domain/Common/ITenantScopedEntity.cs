namespace Sangam.MasterData.Domain.Common;

/// <summary>
/// Marks an entity as owned by exactly one tenant. The DbContext reflects over
/// implementers of this interface to apply the global query filter, so an
/// entity gets tenant isolation on reads purely by implementing it
/// (root CLAUDE.md §6).
/// </summary>
/// <remarks>
/// <c>Gautra</c> is the only entity in this service that implements this.
/// Every other entity here - <c>LookupItem</c>, <c>City</c>, <c>Locality</c> -
/// is platform-wide reference data with no owning Samaaj, the same way
/// identity-tenant-service's own <c>Tenant</c> entity does not implement it.
/// </remarks>
public interface ITenantScopedEntity
{
    Guid TenantId { get; }
}
