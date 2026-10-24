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
    private readonly List<GroupRole> _roles = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>What the group is for. The wireframe's tag: Social Service, Youth, Education.</summary>
    public string? FocusArea { get; private set; }

    /// <summary>
    /// The locality the group serves - a master-data-service id, no referential
    /// integrity across the boundary. Optional: a group may serve the whole
    /// Samaaj. It decides which Area Admin may administer the group, and
    /// nothing about who may join it.
    /// </summary>
    public Guid? LocalityId { get; private set; }

    /// <summary>The title of the President role every group has.</summary>
    public const string PresidentPosition = "President";

    /// <summary>The title of the Volunteer role every member holds until elected to another.</summary>
    public const string VolunteerRoleTitle = "Volunteer";

    /// <summary>
    /// Null until one is set - by a Samaaj admin choosing an existing member,
    /// or by a "President" election. A group may be created without one.
    /// </summary>
    public Guid? PresidentMemberId { get; private set; }

    public bool HasNoPresident => PresidentMemberId is null;

    /// <summary>
    /// The <see cref="Media.GroupPhoto"/> shown as the group page's banner, or
    /// null. Set by uploading one, never by the edit form - two acts, as with a
    /// member's photo.
    /// </summary>
    public Guid? PhotoId { get; private set; }

    public GroupStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<GroupApplication> Applications => _applications.AsReadOnly();
    public IReadOnlyCollection<GroupMember> Members => _members.AsReadOnly();
    public IReadOnlyCollection<GroupRole> Roles => _roles.AsReadOnly();

    /// <summary>The role every member holds until elected to another.</summary>
    public GroupRole VolunteerRole => _roles.Single(r => r.Kind == GroupRoleKind.Volunteer);

    public GroupRole PresidentRole => _roles.Single(r => r.Kind == GroupRoleKind.President);

    public GroupRole? FindRole(Guid roleId) => _roles.FirstOrDefault(r => r.Id == roleId);

    public int HoldersOf(Guid roleId) => _members.Count(m => m.RoleId == roleId);

    private VolunteerGroup() { }   // EF Core

    public static VolunteerGroup Create(
        Guid tenantId,
        string name,
        string? description,
        string? focusArea,
        Guid? presidentMemberId,
        DateTimeOffset createdAt,
        Guid? localityId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (presidentMemberId == Guid.Empty)
        {
            // Null means "not chosen yet" and is allowed. An empty id is a
            // mistake, not an absence.
            throw new ArgumentException(
                "A president id, when given, must not be empty.", nameof(presidentMemberId));
        }

        var group = new VolunteerGroup
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = Normalize(description),
            FocusArea = Normalize(focusArea),
            LocalityId = localityId,
            PresidentMemberId = presidentMemberId,
            Status = GroupStatus.Active,
            CreatedAt = createdAt,
        };

        // The two roles every group has, and neither can be edited or removed.
        group._roles.Add(new GroupRole(group.Id, VolunteerRoleTitle, GroupRoleKind.Volunteer, null, createdAt));
        group._roles.Add(new GroupRole(group.Id, PresidentPosition, GroupRoleKind.President, 1, createdAt));

        // A president, when there is one, is a member of their own group from
        // the start and needs no application to be one. A group created without
        // one starts with no members at all.
        if (presidentMemberId is { } president)
        {
            group._members.Add(new GroupMember(group.Id, president, group.PresidentRole.Id, createdAt));
        }

        group.Raise(new GroupCreatedDomainEvent(
            group.Id, tenantId, presidentMemberId, createdAt));

        return group;
    }

    /// <summary>
    /// Corrects the group's name, description, focus area and locality - a
    /// typo, or the group renaming itself. Does not touch the president,
    /// membership or status; those are each their own decision with their own
    /// command.
    /// </summary>
    public void Update(
        string name, string? description, string? focusArea, DateTimeOffset now, Guid? localityId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Description = Normalize(description);
        FocusArea = Normalize(focusArea);
        LocalityId = localityId;

        Raise(new GroupUpdatedDomainEvent(Id, TenantId, now));
    }

    /// <summary>
    /// Points the group at a newly stored photo and answers which one it
    /// replaced, so the handler can delete it in the same transaction - the
    /// aggregate does not know about the photos table, and silently orphaning
    /// the old row would leave a picture nothing refers to.
    /// </summary>
    public Guid? SetPhoto(Guid photoId, DateTimeOffset now)
    {
        var previous = PhotoId;

        PhotoId = photoId;

        Raise(new GroupUpdatedDomainEvent(Id, TenantId, now));

        return previous;
    }

    /// <summary>Takes the photo down, answering which one it was. Null when there was none.</summary>
    public Guid? RemovePhoto(DateTimeOffset now)
    {
        var removed = PhotoId;

        if (removed is null)
        {
            return null;
        }

        PhotoId = null;

        Raise(new GroupUpdatedDomainEvent(Id, TenantId, now));

        return removed;
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
    /// The president's decision. Accepting also makes the applicant a member,
    /// in the Volunteer role - another is given afterwards, either directly via
    /// <see cref="AssignRole"/> or through an election's own
    /// <see cref="ElectRole"/>. Returns false when there was no pending
    /// application by that id.
    /// </summary>
    public bool DecideApplication(
        Guid applicationId,
        bool accepted,
        Guid decidedBy,
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
            _members.Add(new GroupMember(Id, application.MemberId, VolunteerRole.Id, now));
        }

        Raise(new GroupApplicationDecidedDomainEvent(
            Id, TenantId, application.Id, application.MemberId, decidedBy, accepted, now));

        return true;
    }

    // ---- Volunteer roles ---------------------------------------------------

    /// <summary>
    /// Defines a new role with the seats an election for it will fill.
    /// Refused when another role in this group already has the title - two
    /// "Secretary" roles would make every member's role ambiguous.
    /// </summary>
    public RoleRefusal DefineRole(
        string title, int seats, Guid changedBy, DateTimeOffset now, out GroupRole? role)
    {
        role = null;

        if (TitleTaken(title, exceptRoleId: null))
        {
            return RoleRefusal.DuplicateTitle;
        }

        role = new GroupRole(Id, title, GroupRoleKind.Elected, seats, now);
        _roles.Add(role);

        Raise(new GroupRoleDefinedDomainEvent(Id, TenantId, role.Id, changedBy, now));

        return RoleRefusal.None;
    }

    /// <summary>
    /// Renames a role or changes its seats. Neither built-in role can be, and
    /// seats cannot drop below how many hold it now - that would leave the
    /// group with more holders than the role allows and nobody chosen to go.
    /// </summary>
    public RoleRefusal UpdateRole(Guid roleId, string title, int seats, Guid changedBy, DateTimeOffset now)
    {
        var role = FindRole(roleId);

        if (role is null)
        {
            return RoleRefusal.NotFound;
        }

        if (!role.IsEditable)
        {
            return RoleRefusal.NotEditable;
        }

        if (TitleTaken(title, exceptRoleId: roleId))
        {
            return RoleRefusal.DuplicateTitle;
        }

        if (seats < HoldersOf(roleId))
        {
            return RoleRefusal.SeatsBelowHolders;
        }

        role.Change(title, seats);

        Raise(new GroupRoleUpdatedDomainEvent(Id, TenantId, roleId, changedBy, now));

        return RoleRefusal.None;
    }

    /// <summary>
    /// Removes a role nobody holds. Refused while anybody does: they would be
    /// left holding nothing, and choosing where they go is a decision, not a
    /// side effect.
    /// </summary>
    public RoleRefusal RemoveRole(Guid roleId, Guid changedBy, DateTimeOffset now)
    {
        var role = FindRole(roleId);

        if (role is null)
        {
            return RoleRefusal.NotFound;
        }

        if (!role.IsEditable)
        {
            return RoleRefusal.NotEditable;
        }

        if (HoldersOf(roleId) > 0)
        {
            return RoleRefusal.HasHolders;
        }

        _roles.Remove(role);

        Raise(new GroupRoleRemovedDomainEvent(Id, TenantId, roleId, changedBy, now));

        return RoleRefusal.None;
    }

    /// <summary>
    /// Moves a member into a role directly - the manual override beside an
    /// election, and how somebody is put back to Volunteer.
    /// </summary>
    /// <remarks>
    /// The President role moves only with the presidency itself, by
    /// <see cref="ChangePresident"/> or a President election, in both
    /// directions: the president cannot be assigned another role, and nobody
    /// can be assigned President. Otherwise the title and
    /// <see cref="PresidentMemberId"/> would disagree about who runs the group.
    /// </remarks>
    public RoleRefusal AssignRole(Guid memberId, Guid roleId, DateTimeOffset now)
    {
        var member = _members.FirstOrDefault(m => m.MemberId == memberId);

        if (member is null)
        {
            return RoleRefusal.NotAMember;
        }

        var role = FindRole(roleId);

        if (role is null)
        {
            return RoleRefusal.NotFound;
        }

        if (IsPresident(memberId) || role.Kind == GroupRoleKind.President)
        {
            return RoleRefusal.PresidencyIsSeparate;
        }

        if (member.RoleId == roleId)
        {
            return RoleRefusal.None;
        }

        if (role.Seats is { } seats && HoldersOf(roleId) >= seats)
        {
            return RoleRefusal.RoleFull;
        }

        member.SetRole(roleId);

        Raise(new GroupRolePositionAssignedDomainEvent(Id, TenantId, memberId, role.Title, now, roleId));

        return RoleRefusal.None;
    }

    /// <summary>
    /// Installs the winners of a published election in the role it was run
    /// for. Whoever held that role and did not win goes back to Volunteer.
    /// Returns false when a winner is no longer a member of this group, or is
    /// the president being elected to another role - which the election should
    /// already have prevented, but this method does not trust that promise
    /// from the other side of a cross-aggregate call, and changes nothing.
    /// </summary>
    /// <remarks>
    /// Called only from <c>PublishElectionResultsCommand</c>, after
    /// <c>GroupElection</c> has frozen its winners. A published election is
    /// what actually installs people in a role - not merely an announcement.
    /// </remarks>
    public bool ElectRole(Guid roleId, IReadOnlyList<Guid> winnerMemberIds, DateTimeOffset now)
    {
        var role = FindRole(roleId);

        if (role is null || !role.IsElected || winnerMemberIds.Count == 0)
        {
            return false;
        }

        // Electing the President installs a real president. Only the label
        // moving would leave the winner reading "President" while the previous
        // president kept every power.
        if (role.Kind == GroupRoleKind.President)
        {
            var winner = winnerMemberIds[0];

            return HasMember(winner) && (IsPresident(winner) || ChangePresident(winner, now));
        }

        var winners = new List<GroupMember>(winnerMemberIds.Count);

        foreach (var winnerId in winnerMemberIds)
        {
            var member = _members.FirstOrDefault(m => m.MemberId == winnerId);

            if (member is null || IsPresident(winnerId))
            {
                return false;
            }

            winners.Add(member);
        }

        foreach (var outgoing in _members.Where(m => m.RoleId == roleId && !winnerMemberIds.Contains(m.MemberId)))
        {
            outgoing.SetRole(VolunteerRole.Id);

            Raise(new GroupRolePositionAssignedDomainEvent(
                Id, TenantId, outgoing.MemberId, VolunteerRole.Title, now, VolunteerRole.Id));
        }

        foreach (var winner in winners.Where(w => w.RoleId != roleId))
        {
            winner.SetRole(roleId);

            Raise(new GroupRolePositionAssignedDomainEvent(Id, TenantId, winner.MemberId, role.Title, now, roleId));
        }

        return true;
    }

    private bool TitleTaken(string title, Guid? exceptRoleId) =>
        _roles.Any(r => r.Id != exceptRoleId
            && string.Equals(r.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));

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
            _members.Add(new GroupMember(Id, newPresidentMemberId, PresidentRole.Id, now));
        }
        else
        {
            member.SetRole(PresidentRole.Id);
        }

        // The outgoing president stays in the group, as a Volunteer. Removing
        // them would lose the group its most experienced volunteer as a side
        // effect of an administrative change.
        if (previous is { } outgoing)
        {
            _members.FirstOrDefault(m => m.MemberId == outgoing)?.SetRole(VolunteerRole.Id);
        }

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

/// <summary>Why a role change was refused. <see cref="None"/> is success.</summary>
public enum RoleRefusal
{
    None = 0,
    NotFound,
    NotAMember,
    DuplicateTitle,

    /// <summary>A built-in role - Volunteer or President - which cannot be renamed, resized or removed.</summary>
    NotEditable,
    SeatsBelowHolders,
    HasHolders,
    RoleFull,

    /// <summary>The President role moves only with the presidency itself.</summary>
    PresidencyIsSeparate,
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
