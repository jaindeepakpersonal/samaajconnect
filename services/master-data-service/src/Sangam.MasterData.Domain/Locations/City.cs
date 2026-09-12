using Sangam.MasterData.Domain.Common;
using Sangam.MasterData.Domain.Locations.Events;

namespace Sangam.MasterData.Domain.Locations;

/// <summary>
/// A city, scoped to a country. Own aggregate rather than a row in
/// <c>LookupItem</c> - unlike the four flat categories there, a city carries a
/// parent (<see cref="CountryId"/>), which that table's shape has no column
/// for.
/// </summary>
public sealed class City : AggregateRoot
{
    public Guid Id { get; private set; }

    /// <summary>The owning <c>LookupItem</c> whose <c>Category</c> is <c>Country</c>.</summary>
    public Guid CountryId { get; private set; }

    /// <summary>
    /// The owning <see cref="State"/>, or null. Added 2026-09-12, after every
    /// city that already existed - nullable rather than a breaking migration,
    /// since a city named before <see cref="State"/> existed is not wrong for
    /// having no state on it.
    /// </summary>
    public Guid? StateId { get; private set; }

    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }

    private City() { }   // EF Core

    public static City Create(Guid countryId, Guid? stateId, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var city = new City
        {
            Id = Guid.NewGuid(),
            CountryId = countryId,
            StateId = stateId,
            Name = name.Trim(),
            IsActive = true,
        };

        city.Raise(new CityCreatedDomainEvent(city.Id, city.CountryId, city.Name, now));

        return city;
    }

    public void Update(Guid? stateId, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        StateId = stateId;
        Name = name.Trim();

        Raise(new CityUpdatedDomainEvent(Id, CountryId, Name, now));
    }

    /// <summary>Deactivates, never deletes - see <c>LookupItem.Deactivate</c> for why.</summary>
    public bool Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;

        Raise(new CityDeactivatedDomainEvent(Id, now));

        return true;
    }
}
