namespace Sangam.MemberFamily.Application.Children;

public sealed record ChildResponse(
    Guid Id,
    Guid FamilyId,
    string FullName,
    DateOnly DateOfBirth,
    int Age,
    string Gender,
    string? PhotoUrl,
    string Status,
    bool IsEligibleForConversion,
    bool HasPendingConversion,
    DateTimeOffset CreatedAt);

public sealed record ConversionRequestResponse(
    Guid Id,
    Guid ChildProfileId,
    string ChildFullName,
    string MobileOrEmail,
    string Status,
    DateTimeOffset RequestedAt,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionNote);
