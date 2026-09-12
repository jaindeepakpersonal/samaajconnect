namespace Sangam.Businesses.Application.Businesses;

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    int PriceInPaise,
    bool IsAvailable);

/// <summary>
/// A business as either its owner or a directory viewer sees it.
/// <see cref="Products"/> carries every product to the owner and only the
/// available ones to anyone else - decided by the mapping call, not by two
/// separate response shapes.
/// </summary>
public sealed record BusinessResponse(
    Guid Id,
    Guid OwnerMemberId,
    string Name,
    Guid CategoryId,
    Guid? CityId,
    Guid? LocalityId,
    string? Description,
    string? ContactPhone,
    string? ContactEmail,
    bool IsActive,
    IReadOnlyList<ProductResponse> Products,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
