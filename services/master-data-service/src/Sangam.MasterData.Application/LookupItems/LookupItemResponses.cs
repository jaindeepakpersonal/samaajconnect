namespace Sangam.MasterData.Application.LookupItems;

public sealed record LookupItemResponse(
    Guid Id, string Category, string Code, string Label, int SortOrder, bool IsActive);
