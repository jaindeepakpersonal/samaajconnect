namespace Sangam.MasterData.Application.Cities;

public sealed record CityResponse(Guid Id, Guid CountryId, Guid? StateId, string Name, bool IsActive);
