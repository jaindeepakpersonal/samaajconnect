namespace Sangam.VolunteerGroups.Application.Groups;

/// <summary>
/// A group as the list and detail screens show it.
/// </summary>
/// <remarks>
/// <paramref name="PresidentMemberId"/> is an id, not a name. Names live in
/// member-family-service, and resolving one here would mean a call per group
/// for a list - the synchronous reach across a service boundary this repo
/// avoids. The portals already load the member directory and can map ids to
/// names client-side.
/// </remarks>
public sealed record GroupResponse(
    Guid Id,
    string Name,
    string? Description,
    string? FocusArea,
    Guid PresidentMemberId,
    string Status,
    int MemberCount,

    /// <summary>Pending applications. Zero for anyone who cannot decide them.</summary>
    int PendingApplicationCount,

    /// <summary>What the asking member's own application says, if they have one.</summary>
    string? MyApplicationStatus,

    /// <summary>Whether the asking member is in this group.</summary>
    bool IAmAMember,
    bool IAmThePresident,
    DateTimeOffset CreatedAt);

public sealed record GroupMemberResponse(
    Guid MemberId,
    string? RolePosition,
    DateTimeOffset JoinedAt);

public sealed record GroupApplicationResponse(
    Guid Id,
    Guid MemberId,
    string? Note,
    string Status,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// A group with its members. Applications are fetched separately, because only
/// the president may read them and a detail screen everyone can open should not
/// carry them.
/// </summary>
public sealed record GroupDetailResponse(
    GroupResponse Group,
    IReadOnlyList<GroupMemberResponse> Members);
