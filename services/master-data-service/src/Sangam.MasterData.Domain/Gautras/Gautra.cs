using Sangam.MasterData.Domain.Common;
using Sangam.MasterData.Domain.Gautras.Events;

namespace Sangam.MasterData.Domain.Gautras;

/// <summary>
/// A Gautra (clan/lineage name) in one Samaaj's own list.
/// </summary>
/// <remarks>
/// The one tenant-scoped entity in this service. Every other aggregate here
/// (<c>LookupItem</c>, <c>City</c>, <c>Locality</c>) is platform-wide; a
/// Gautra list is specific to a Samaaj, which is the "Samaaj-wise" part of
/// the requirement this service exists for. It gets the same global tenant
/// query filter as every other tenant-owned entity on the platform
/// (root CLAUDE.md §6) purely by implementing <see cref="ITenantScopedEntity"/>.
/// </remarks>
public sealed class Gautra : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }

    private Gautra() { }   // EF Core

    public static Gautra Create(Guid tenantId, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var gautra = new Gautra
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            IsActive = true,
        };

        gautra.Raise(new GautraCreatedDomainEvent(gautra.Id, gautra.TenantId, gautra.Name, now));

        return gautra;
    }

    public void Update(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();

        Raise(new GautraUpdatedDomainEvent(Id, TenantId, Name, now));
    }

    /// <summary>Deactivates, never deletes - see <c>LookupItem.Deactivate</c> for why.</summary>
    public bool Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;

        Raise(new GautraDeactivatedDomainEvent(Id, TenantId, now));

        return true;
    }
}
