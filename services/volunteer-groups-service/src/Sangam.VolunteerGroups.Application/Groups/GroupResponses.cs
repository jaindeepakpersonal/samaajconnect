namespace Sangam.VolunteerGroups.Application.Groups;

/// <summary>
/// A group as the list and detail screens show it.
/// </summary>
/// <remarks>
/// <paramref name="PresidentMemberId"/> is an id, not a name, and null until a president is set. Names live in
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

    /// <summary>The locality the group serves, or null for the whole Samaaj.</summary>
    Guid? LocalityId,
    Guid? PresidentMemberId,
    string Status,
    int MemberCount,

    /// <summary>Pending applications. Zero for anyone who cannot decide them.</summary>
    int PendingApplicationCount,

    /// <summary>What the asking member's own application says, if they have one.</summary>
    string? MyApplicationStatus,

    /// <summary>Whether the asking member is in this group.</summary>
    bool IAmAMember,
    bool IAmThePresident,

    /// <summary>
    /// Whether the asking member may decide join requests and run elections: the
    /// president, or an admin while the group has none. Portals read this rather
    /// than re-deriving it from <c>IAmThePresident</c> and a role.
    /// </summary>
    bool ICanLead,
    DateTimeOffset CreatedAt,

    /// <summary>
    /// Whether the asking member may define roles, assign them, and run
    /// elections: the president, or an administrator of this group at any time.
    /// </summary>
    bool ICanRunElections);

/// <remarks><c>RolePosition</c> is the role's title, kept under its old name for the screens that read it.</remarks>
public sealed record GroupMemberResponse(
    Guid MemberId,
    string? RolePosition,
    DateTimeOffset JoinedAt,
    Guid RoleId);

/// <summary>
/// One of the group's volunteer roles. <c>Kind</c> is Volunteer, President or
/// Elected; only Elected roles are <c>IsEditable</c>, and every role but
/// Volunteer can have an election. <c>Seats</c> is null for Volunteer: no limit.
/// </summary>
public sealed record GroupRoleResponse(
    Guid Id,
    string Title,
    string Kind,
    int? Seats,
    int HolderCount,
    bool IsEditable,
    bool IsElected);

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
    IReadOnlyList<GroupMemberResponse> Members,
    IReadOnlyList<GroupRoleResponse> Roles);
