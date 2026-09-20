namespace Sangam.Trust.Application.Trusts;

/// <summary>A Trust as the list and detail screens show it.</summary>
public sealed record TrustResponse(
    Guid Id,
    Guid LocalityId,
    string Name,
    string Status,
    DateTimeOffset CreatedAt);
