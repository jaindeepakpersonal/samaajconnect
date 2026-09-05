using Sangam.VolunteerGroups.Domain.Common;

namespace Sangam.VolunteerGroups.Domain.Groups;

/// <summary>
/// A volunteer group within one Samaaj: a Seva group, a Yuva Mandal, an
/// education group.
/// </summary>
/// <remarks>
/// The join flow is the substance of this aggregate, and it is deliberately
/// asymmetric. Applying is a request; being accepted is a decision, and the
/// decision belongs to the group's president rather than to a Samaaj admin.
/// A Samaaj admin creates the group and names its president; who is *in* it is
/// the president's business, exactly as deciding a family's join requests is
/// the family head's rather than an administrator's.
///
/// Membership and applications are separate things. An application is a
/// standing request with an outcome; a <see cref="GroupMember"/> is someone who
/// is in the group. Accepting turns the first into the second, and leaving
/// removes the membership without erasing the application that granted it -
/// "were they ever accepted?" stays answerable.
/// </remarks>
public sealed class VolunteerGroup : AggregateRoot, ITenantScopedEntity
{
    private readonly List<GroupApplication> _applications = [];
    private readonly List<GroupMember> _members = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>What the group is for. The wireframe's tag: Social Service, Youth, Education.</summary>
    public string? FocusArea { get; private set; }

    public Guid PresidentMemberId { get; private set; }
    public GroupStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<GroupApplication> Applications => _applications.AsReadOnly();
    public IReadOnlyCollection<GroupMember> Members => _members.AsReadOnly();

    private VolunteerGroup() { }   // EF Core

    public static VolunteerGroup Create(
        Guid tenantId,
        string name,
        string? description,
        string? focusArea,
        Guid presidentMemberId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (presidentMemberId == Guid.Empty)
        {
            // A group with no president has nobody to decide its applications,
            // so every request to join would queue forever with no way to tell
            // that is what was happening.
            throw new ArgumentException(
                "A volunteer group must have a president.", nameof(presidentMemberId));
        }

        var group = new VolunteerGroup
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = Normalize(description),
            FocusArea = Normalize(focusArea),
            PresidentMemberId = presidentMemberId,
            Status = GroupStatus.Active,
            CreatedAt = createdAt,
        };

        // The president is a member of their own group from the start, and
        // needs no application to be one.
        group._members.Add(new GroupMember(group.Id, presidentMemberId, "President", createdAt));

        group.Raise(new GroupCreatedDomainEvent(
            group.Id, tenantId, presidentMemberId, createdAt));

        return group;
    }

    public bool IsPresident(Guid memberId) => PresidentMemberId == memberId;

    public bool HasMember(Guid memberId) => _members.Any(m => m.MemberId == memberId);

    public GroupApplication? FindApplication(Guid applicationId) =>
        _applications.FirstOrDefault(a => a.Id == applicationId);

    /// <summary>
    /// Records a request to join. Returns null when there is nothing to do -
    /// the member is already in, or already has a request outstanding - so a
    /// repeated click is a no-op rather than a second row for the president to
    /// decide twice.
    /// </summary>
    /// <remarks>
    /// A previously *rejected* application may be made again. Circumstances and
    /// minds both change, and a permanent bar from one refusal is a heavier
    /// consequence than a president was choosing at the time. The old row is
    /// replaced rather than reused, so the queue shows one live request.
    /// </remarks>
    public GroupApplication? Apply(Guid memberId, string? note, DateTimeOffset now)
    {
        if (Status != GroupStatus.Active || HasMember(memberId))
        {
            return null;
        }

        var existing = _applications.FirstOrDefault(a => a.MemberId == memberId);

        if (existing is not null)
        {
            if (existing.Status == ApplicationStatus.Pending)
            {
                return null;
            }

            _applications.Remove(existing);
        }

        var application = new GroupApplication(Id, memberId, Normalize(note), now);

        _applications.Add(application);

        Raise(new GroupApplicationSubmittedDomainEvent(
            Id, TenantId, application.Id, memberId, now));

        return application;
    }

    /// <summary>
    /// The president's decision. Accepting also makes the applicant a member.
    /// Returns false when there was no pending application by that id.
    /// </summary>
    public bool DecideApplication(
        Guid applicationId,
        bool accepted,
        Guid decidedBy,
        string? rolePosition,
        DateTimeOffset now)
    {
        var application = _applications.FirstOrDefault(
            a => a.Id == applicationId && a.Status == ApplicationStatus.Pending);

        if (application is null)
        {
            return false;
        }

        application.Decide(accepted, decidedBy, now);

        if (accepted && !HasMember(application.MemberId))
        {
            _members.Add(new GroupMember(
                Id, application.MemberId, Normalize(rolePosition), now));
        }

        Raise(new GroupApplicationDecidedDomainEvent(
            Id, TenantId, application.Id, application.MemberId, decidedBy, accepted, now));

        return true;
    }

    /// <summary>
    /// Gives a member a position within the group - Secretary, Coordinator.
    /// Returns false when they are not in it.
    /// </summary>
    /// <remarks>
    /// A free-text label, not a platform role. What someone is called inside a
    /// Seva group grants nothing anywhere and should not need a deployment to
    /// add; the platform roles in <c>AuthorizationCatalog</c> are what actually
    /// gate, and they are deliberately a closed list.
    /// </remarks>
    public bool AssignRolePosition(Guid memberId, string? rolePosition, DateTimeOffset now)
    {
        var member = _members.FirstOrDefault(m => m.MemberId == memberId);

        if (member is null)
        {
            return false;
        }

        member.SetRolePosition(Normalize(rolePosition));

        Raise(new GroupRolePositionAssignedDomainEvent(
            Id, TenantId, memberId, member.RolePosition, now));

        return true;
    }

    /// <summary>
    /// Removes a member. Returns false when they were not in the group, or when
    /// they are the president.
    /// </summary>
    /// <remarks>
    /// The president cannot be removed from their own group, because a group
    /// whose president is not a member of it has nobody able to decide its
    /// applications. Replacing a president is a Samaaj admin's job, and is its
    /// own decision - see <see cref="ChangePresident"/>.
    /// </remarks>
    public bool RemoveMember(Guid memberId, Guid removedBy, DateTimeOffset now)
    {
        if (IsPresident(memberId))
        {
            return false;
        }

        var member = _members.FirstOrDefault(m => m.MemberId == memberId);

        if (member is null)
        {
            return false;
        }

        _members.Remove(member);

        Raise(new GroupMemberRemovedDomainEvent(Id, TenantId, memberId, removedBy, now));

        return true;
    }

    /// <summary>Hands the group to a different president, who joins it if they were not in it.</summary>
    public bool ChangePresident(Guid newPresidentMemberId, DateTimeOffset now)
    {
        if (newPresidentMemberId == Guid.Empty || newPresidentMemberId == PresidentMemberId)
        {
            return false;
        }

        var previous = PresidentMemberId;
        PresidentMemberId = newPresidentMemberId;

        var member = _members.FirstOrDefault(m => m.MemberId == newPresidentMemberId);

        if (member is null)
        {
            _members.Add(new GroupMember(Id, newPresidentMemberId, "President", now));
        }
        else
        {
            member.SetRolePosition("President");
        }

        // The outgoing president stays in the group as an ordinary member.
        // Removing them would lose the group its most experienced volunteer as
        // a side effect of an administrative change.
        _members.FirstOrDefault(m => m.MemberId == previous)?.SetRolePosition(null);

        Raise(new GroupPresidentChangedDomainEvent(
            Id, TenantId, previous, newPresidentMemberId, now));

        return true;
    }

    /// <summary>
    /// Activates or deactivates the group. Returns false when it is already in
    /// that state, so a repeated click is not a second audit entry.
    /// </summary>
    public bool ChangeStatus(GroupStatus status, DateTimeOffset now)
    {
        if (Status == status)
        {
            return false;
        }

        var previous = Status;
        Status = status;

        Raise(new GroupStatusChangedDomainEvent(
            Id, TenantId, previous.ToString(), status.ToString(), now));

        return true;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum GroupStatus
{
    Active = 1,

    /// <summary>Still visible and still has its members; takes no new applications.</summary>
    Inactive = 2,
}

public enum ApplicationStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3,
}
