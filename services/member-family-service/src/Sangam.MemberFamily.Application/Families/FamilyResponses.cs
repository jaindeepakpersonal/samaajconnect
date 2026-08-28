namespace Sangam.MemberFamily.Application.Families;

public sealed record FamilyMemberResponse(
    Guid Id,
    Guid MemberProfileId,
    string FullName,
    string Relationship,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt);

/// <summary>
/// <paramref name="FamilyCode"/> is present only for the head. It is the token
/// anyone needs to request to join, so handing it to every member would mean
/// any one of them could invite the whole Samaaj into the household.
/// </summary>
public sealed record FamilyResponse(
    Guid Id,
    Guid FamilyHeadMemberId,
    string? FamilyCode,
    bool ViewerIsHead,
    IReadOnlyList<FamilyMemberResponse> Members,
    DateTimeOffset CreatedAt);
