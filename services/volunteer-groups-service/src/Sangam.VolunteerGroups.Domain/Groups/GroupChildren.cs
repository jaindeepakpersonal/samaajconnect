namespace Sangam.VolunteerGroups.Domain.Groups;

/// <summary>
/// A request to join, and its outcome. Owned by <see cref="VolunteerGroup"/>,
/// which is why it has no independent factory.
/// </summary>
/// <remarks>
/// Kept after the decision rather than deleted. "Were they ever accepted, and
/// by whom?" is a question a group president will be asked, and it needs an
/// answer that does not depend on somebody remembering.
/// </remarks>
public sealed class GroupApplication
{
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid MemberId { get; private set; }

    /// <summary>What the applicant said for themselves. Optional.</summary>
    public string? Note { get; private set; }

    public ApplicationStatus Status { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private GroupApplication() { }   // EF Core

    internal GroupApplication(Guid groupId, Guid memberId, string? note, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        GroupId = groupId;
        MemberId = memberId;
        Note = note;
        Status = ApplicationStatus.Pending;
        CreatedAt = createdAt;
    }

    internal void Decide(bool accepted, Guid decidedBy, DateTimeOffset now)
    {
        Status = accepted ? ApplicationStatus.Accepted : ApplicationStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAt = now;
    }
}

/// <summary>Somebody who is in the group.</summary>
public sealed class GroupMember
{
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid MemberId { get; private set; }

    /// <summary>
    /// The <see cref="GroupRole"/> they hold - every member holds exactly one,
    /// starting with the group's Volunteer role. Deliberately not a platform
    /// role: it grants nothing anywhere outside this group.
    /// </summary>
    public Guid RoleId { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    private GroupMember() { }   // EF Core

    internal GroupMember(Guid groupId, Guid memberId, Guid roleId, DateTimeOffset joinedAt)
    {
        Id = Guid.NewGuid();
        GroupId = groupId;
        MemberId = memberId;
        RoleId = roleId;
        JoinedAt = joinedAt;
    }

    internal void SetRole(Guid roleId) => RoleId = roleId;
}

/// <summary>
/// A volunteer role inside one group - Volunteer, President, Secretary,
/// Coordinator - and how many people may hold it at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Defined per group, not a closed platform list,</b> because what a Seva
/// group calls its people should not need a deployment to add. It is not a
/// platform role either: it grants nothing outside the group. Holding the
/// President role is the one exception with teeth, and only because
/// <see cref="VolunteerGroup.PresidentMemberId"/> moves with it.
/// </para>
/// <para>
/// Two roles exist in every group and cannot be edited or removed: the
/// Volunteer role everybody holds until elected to something else, with no
/// seat limit and no election; and the President role, one seat. Every other
/// role is <see cref="GroupRoleKind.Elected"/>, with the seat count an
/// election for it fills.
/// </para>
/// </remarks>
public sealed class GroupRole
{
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public string Title { get; private set; } = null!;
    public GroupRoleKind Kind { get; private set; }

    /// <summary>How many members may hold it at once. Null only for the Volunteer role: no limit.</summary>
    public int? Seats { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private GroupRole() { }   // EF Core

    internal GroupRole(Guid groupId, string title, GroupRoleKind kind, int? seats, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        GroupId = groupId;
        Title = title.Trim();
        Kind = kind;
        Seats = seats;
        CreatedAt = createdAt;
    }

    /// <summary>Whether a group can run an election for it - every role but Volunteer.</summary>
    public bool IsElected => Kind is not GroupRoleKind.Volunteer;

    /// <summary>Whether the group may rename, resize or remove it - neither built-in role.</summary>
    public bool IsEditable => Kind == GroupRoleKind.Elected;

    internal void Change(string title, int seats)
    {
        Title = title.Trim();
        Seats = seats;
    }
}

public enum GroupRoleKind
{
    /// <summary>Everybody's role until elected to another. No seat limit, never elected.</summary>
    Volunteer = 1,

    /// <summary>One seat. Holding it is being <see cref="VolunteerGroup.PresidentMemberId"/>.</summary>
    President = 2,

    /// <summary>A role the group defined, with its own seat count.</summary>
    Elected = 3,
}
