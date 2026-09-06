using Sangam.MasterData.Domain.Gautras;

namespace Sangam.MasterData.Application.Gautras;

internal static class GautraMappings
{
    public static GautraResponse ToResponse(this Gautra gautra) =>
        new(gautra.Id, gautra.Name, gautra.IsActive);
}
