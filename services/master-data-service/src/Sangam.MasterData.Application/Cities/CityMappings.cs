using Sangam.MasterData.Domain.Locations;

namespace Sangam.MasterData.Application.Cities;

internal static class CityMappings
{
    public static CityResponse ToResponse(this City city) =>
        new(city.Id, city.CountryId, city.Name, city.IsActive);
}
