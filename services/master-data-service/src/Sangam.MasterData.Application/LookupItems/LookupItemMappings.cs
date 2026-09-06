using Sangam.MasterData.Domain.Lookups;

namespace Sangam.MasterData.Application.LookupItems;

internal static class LookupItemMappings
{
    public static LookupItemResponse ToResponse(this LookupItem item) => new(
        item.Id, item.Category.ToString(), item.Code, item.Label, item.SortOrder, item.IsActive);
}
