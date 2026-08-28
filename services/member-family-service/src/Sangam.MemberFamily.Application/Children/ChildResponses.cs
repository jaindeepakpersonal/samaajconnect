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
    DateTimeOffset CreatedAt,
    ParentalConsentResponse? ParentalConsent);

/// <summary>
/// The consent this record rests on. Shown back so a family can see what was
/// agreed and when, rather than having to take it on trust.
/// </summary>
public sealed record ParentalConsentResponse(
    Guid GivenByMemberId,
    string NoticeVersion,
    string Attestation,
    DateTimeOffset GivenAt);

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
