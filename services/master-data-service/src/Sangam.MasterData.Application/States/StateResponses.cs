namespace Sangam.MasterData.Application.States;

public sealed record StateResponse(Guid Id, Guid CountryId, string Name, bool IsActive);
