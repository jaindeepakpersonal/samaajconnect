namespace Sangam.MasterData.Application.Localities;

public sealed record LocalityResponse(Guid Id, Guid CityId, string Name, bool IsActive);
