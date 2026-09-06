using Sangam.MasterData.Domain.Locations;

namespace Sangam.MasterData.Application.Localities;

internal static class LocalityMappings
{
    public static LocalityResponse ToResponse(this Locality locality) =>
        new(locality.Id, locality.CityId, locality.Name, locality.IsActive);
}
