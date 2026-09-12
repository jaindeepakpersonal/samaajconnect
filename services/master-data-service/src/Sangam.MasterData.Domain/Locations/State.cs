using Sangam.MasterData.Domain.Common;
using Sangam.MasterData.Domain.Locations.Events;

namespace Sangam.MasterData.Domain.Locations;

/// <summary>
/// A state or province, scoped to a country. Own aggregate rather than a row
/// in <c>LookupItem</c> - the same reason <see cref="City"/> is: it carries a
/// parent (<see cref="CountryId"/>), which that flat table's shape has no
/// column for.
/// </summary>
/// <remarks>
/// Sits between <see cref="Domain.Lookups.LookupItem"/>'s <c>Country</c>
/// category and <see cref="City"/> in the location hierarchy
/// (Country → State → City → Locality). <see cref="City.StateId"/> is
/// nullable rather than this being a breaking change: every city that existed
/// before this aggregate did, and remains valid, with no state named.
/// </remarks>
public sealed class State : AggregateRoot
{
    public Guid Id { get; private set; }

    /// <summary>The owning <c>LookupItem</c> whose <c>Category</c> is <c>Country</c>.</summary>
    public Guid CountryId { get; private set; }

    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }

    private State() { }   // EF Core

    public static State Create(Guid countryId, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var state = new State
        {
            Id = Guid.NewGuid(),
            CountryId = countryId,
            Name = name.Trim(),
            IsActive = true,
        };

        state.Raise(new StateCreatedDomainEvent(state.Id, state.CountryId, state.Name, now));

        return state;
    }

    public void Update(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();

        Raise(new StateUpdatedDomainEvent(Id, CountryId, Name, now));
    }

    /// <summary>Deactivates, never deletes - see <c>LookupItem.Deactivate</c> for why.</summary>
    public bool Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;

        Raise(new StateDeactivatedDomainEvent(Id, now));

        return true;
    }
}
