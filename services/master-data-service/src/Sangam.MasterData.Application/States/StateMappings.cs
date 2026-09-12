using Sangam.MasterData.Domain.Locations;

namespace Sangam.MasterData.Application.States;

internal static class StateMappings
{
    public static StateResponse ToResponse(this State state) =>
        new(state.Id, state.CountryId, state.Name, state.IsActive);
}
