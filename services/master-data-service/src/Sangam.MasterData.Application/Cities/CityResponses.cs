namespace Sangam.MasterData.Application.Cities;

public sealed record CityResponse(Guid Id, Guid CountryId, string Name, bool IsActive);
