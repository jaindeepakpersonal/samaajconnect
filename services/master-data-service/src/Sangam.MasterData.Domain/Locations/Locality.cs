using Sangam.MasterData.Domain.Common;
using Sangam.MasterData.Domain.Locations.Events;

namespace Sangam.MasterData.Domain.Locations;

/// <summary>
/// An area/locality within a city - this is the "locations master" (e.g.
/// Hiran Magri, Surajpole, Shobhagpura in the member-portal wireframe's
/// existing locality dropdown). Own aggregate for the same reason as
/// <see cref="City"/>: it carries a parent id <see cref="CityId"/>.
/// </summary>
public sealed class Locality : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid CityId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }

    private Locality() { }   // EF Core

    public static Locality Create(Guid cityId, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var locality = new Locality
        {
            Id = Guid.NewGuid(),
            CityId = cityId,
            Name = name.Trim(),
            IsActive = true,
        };

        locality.Raise(new LocalityCreatedDomainEvent(locality.Id, locality.CityId, locality.Name, now));

        return locality;
    }

    public void Update(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();

        Raise(new LocalityUpdatedDomainEvent(Id, CityId, Name, now));
    }

    public bool Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;

        Raise(new LocalityDeactivatedDomainEvent(Id, now));

        return true;
    }
}
