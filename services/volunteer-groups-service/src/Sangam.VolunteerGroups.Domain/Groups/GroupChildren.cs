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
    /// What they are called inside this group - Secretary, Coordinator. Free
    /// text, and deliberately not a platform role: it grants nothing anywhere.
    /// </summary>
    public string? RolePosition { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    private GroupMember() { }   // EF Core

    internal GroupMember(Guid groupId, Guid memberId, string? rolePosition, DateTimeOffset joinedAt)
    {
        Id = Guid.NewGuid();
        GroupId = groupId;
        MemberId = memberId;
        RolePosition = rolePosition;
        JoinedAt = joinedAt;
    }

    internal void SetRolePosition(string? rolePosition) => RolePosition = rolePosition;
}
