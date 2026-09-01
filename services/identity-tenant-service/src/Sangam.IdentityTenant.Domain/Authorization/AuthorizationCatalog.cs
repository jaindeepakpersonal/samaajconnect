namespace Sangam.IdentityTenant.Domain.Authorization;

/// <summary>
/// The platform's fixed roles and permissions, with stable ids.
/// </summary>
/// <remarks>
/// The ids are hand-assigned rather than generated so seed data is identical in
/// every environment: a role grant written in one database means the same thing
/// in another, and re-running a migration never produces a second copy of a
/// role under a new id. The literal shape (a... for roles, b... for
/// permissions) makes a seeded id obvious on sight in a query result.
/// </remarks>
public static class AuthorizationCatalog
{
    public static class RoleIds
    {
        public static readonly Guid SuperAdmin = new("a0000000-0000-0000-0000-000000000001");
        public static readonly Guid SamaajAdmin = new("a0000000-0000-0000-0000-000000000002");
        public static readonly Guid Member = new("a0000000-0000-0000-0000-000000000003");
        public static readonly Guid FamilyHead = new("a0000000-0000-0000-0000-000000000004");
        public static readonly Guid VolunteerGroupPresident = new("a0000000-0000-0000-0000-000000000005");
        public static readonly Guid PathshalaTeacher = new("a0000000-0000-0000-0000-000000000006");
        public static readonly Guid PathshalaStudent = new("a0000000-0000-0000-0000-000000000007");
        public static readonly Guid ContentModerator = new("a0000000-0000-0000-0000-000000000008");
        public static readonly Guid BoliManager = new("a0000000-0000-0000-0000-000000000009");
    }

    public static class PermissionIds
    {
        public static readonly Guid TenantManage = new("b0000000-0000-0000-0000-000000000001");
        public static readonly Guid AdminUsersManage = new("b0000000-0000-0000-0000-000000000002");
        public static readonly Guid MembersRead = new("b0000000-0000-0000-0000-000000000003");
        public static readonly Guid MembersWrite = new("b0000000-0000-0000-0000-000000000004");
        public static readonly Guid FamilyWrite = new("b0000000-0000-0000-0000-000000000005");
        public static readonly Guid FamilyApproveConversion = new("b0000000-0000-0000-0000-000000000006");
        public static readonly Guid TimelinePost = new("b0000000-0000-0000-0000-000000000007");
        public static readonly Guid TimelineModerate = new("b0000000-0000-0000-0000-000000000008");
        public static readonly Guid VolunteerGroupsManage = new("b0000000-0000-0000-0000-000000000009");
        public static readonly Guid EventsPublish = new("b0000000-0000-0000-0000-00000000000a");
        public static readonly Guid SocialIssuesApprove = new("b0000000-0000-0000-0000-00000000000b");
        public static readonly Guid CelebrityVotingConfigure = new("b0000000-0000-0000-0000-00000000000c");
        public static readonly Guid PathshalaManage = new("b0000000-0000-0000-0000-00000000000d");
        public static readonly Guid PathshalaAttendanceWrite = new("b0000000-0000-0000-0000-00000000000e");
        public static readonly Guid PathshalaExamsWrite = new("b0000000-0000-0000-0000-00000000000f");
        public static readonly Guid BoliManage = new("b0000000-0000-0000-0000-000000000010");
        public static readonly Guid BoliPublishResults = new("b0000000-0000-0000-0000-000000000011");
        public static readonly Guid AuditRead = new("b0000000-0000-0000-0000-000000000012");
        public static readonly Guid VolunteerGroupsLead = new("b0000000-0000-0000-0000-000000000013");
        public static readonly Guid RolesManage = new("b0000000-0000-0000-0000-000000000014");
        public static readonly Guid NotificationsBroadcast = new("b0000000-0000-0000-0000-000000000015");
    }

    public static IReadOnlyList<Role> Roles { get; } =
    [
        new(RoleIds.SuperAdmin, "SuperAdmin"),
        new(RoleIds.SamaajAdmin, "SamaajAdmin"),
        new(RoleIds.Member, "Member"),
        new(RoleIds.FamilyHead, "FamilyHead"),
        new(RoleIds.VolunteerGroupPresident, "VolunteerGroupPresident"),
        new(RoleIds.PathshalaTeacher, "PathshalaTeacher"),
        new(RoleIds.PathshalaStudent, "PathshalaStudent"),
        new(RoleIds.ContentModerator, "ContentModerator"),
        new(RoleIds.BoliManager, "BoliManager"),
    ];

    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        new(PermissionIds.TenantManage, "Tenant.Manage"),
        new(PermissionIds.AdminUsersManage, "AdminUsers.Manage"),
        new(PermissionIds.MembersRead, "Members.Read"),
        new(PermissionIds.MembersWrite, "Members.Write"),
        new(PermissionIds.FamilyWrite, "Family.Write"),
        new(PermissionIds.FamilyApproveConversion, "Family.ApproveConversion"),
        new(PermissionIds.TimelinePost, "Timeline.Post"),
        new(PermissionIds.TimelineModerate, "Timeline.Moderate"),
        new(PermissionIds.VolunteerGroupsManage, "VolunteerGroups.Manage"),
        new(PermissionIds.EventsPublish, "Events.Publish"),
        new(PermissionIds.SocialIssuesApprove, "SocialIssues.Approve"),
        new(PermissionIds.CelebrityVotingConfigure, "CelebrityVoting.Configure"),
        new(PermissionIds.PathshalaManage, "Pathshala.Manage"),
        new(PermissionIds.PathshalaAttendanceWrite, "Pathshala.Attendance.Write"),
        new(PermissionIds.PathshalaExamsWrite, "Pathshala.Exams.Write"),
        new(PermissionIds.BoliManage, "Boli.Manage"),
        new(PermissionIds.BoliPublishResults, "Boli.PublishResults"),
        new(PermissionIds.AuditRead, "Audit.Read"),
        new(PermissionIds.VolunteerGroupsLead, "VolunteerGroups.Lead"),
        new(PermissionIds.RolesManage, "Roles.Manage"),
        new(PermissionIds.NotificationsBroadcast, "Notifications.Broadcast"),
    ];

    /// <summary>
    /// Which permissions each role carries. Super Admin holds every permission
    /// by construction rather than through a bypass check, so tightening what a
    /// Super Admin may do stays a data change instead of a code change.
    /// </summary>
    public static IReadOnlyList<RolePermission> RolePermissions { get; } =
    [
        .. Permissions.Select(p => new RolePermission(RoleIds.SuperAdmin, p.Id)),

        new(RoleIds.SamaajAdmin, PermissionIds.AdminUsersManage),
        new(RoleIds.SamaajAdmin, PermissionIds.MembersRead),
        new(RoleIds.SamaajAdmin, PermissionIds.MembersWrite),
        new(RoleIds.SamaajAdmin, PermissionIds.FamilyWrite),
        new(RoleIds.SamaajAdmin, PermissionIds.FamilyApproveConversion),
        new(RoleIds.SamaajAdmin, PermissionIds.TimelinePost),
        new(RoleIds.SamaajAdmin, PermissionIds.TimelineModerate),
        new(RoleIds.SamaajAdmin, PermissionIds.VolunteerGroupsManage),
        new(RoleIds.SamaajAdmin, PermissionIds.VolunteerGroupsLead),
        new(RoleIds.SamaajAdmin, PermissionIds.EventsPublish),
        new(RoleIds.SamaajAdmin, PermissionIds.SocialIssuesApprove),
        new(RoleIds.SamaajAdmin, PermissionIds.CelebrityVotingConfigure),

        // Running a Pathshala - sessions, classes, teacher assignments, placing
        // students - is the Samaaj's job. Only *creating* the master record is
        // reserved to a Super Admin (DATA-MODEL.md section 9), and that is
        // enforced by a role check on the one command rather than by withholding
        // this permission, which would have left every other Pathshala operation
        // reachable by nobody but the platform operator.
        new(RoleIds.SamaajAdmin, PermissionIds.PathshalaManage),
        new(RoleIds.SamaajAdmin, PermissionIds.BoliManage),
        new(RoleIds.SamaajAdmin, PermissionIds.BoliPublishResults),
        new(RoleIds.SamaajAdmin, PermissionIds.AuditRead),
        new(RoleIds.SamaajAdmin, PermissionIds.RolesManage),

        // Announcing to the whole Samaaj at once. Its own key rather than folded
        // into AdminUsers.Manage, because "may administer members" and "may put
        // a message in front of every one of them" are different powers, and a
        // Samaaj that wants to hand the second to a content moderator without
        // the first should be able to - which the role matrix now allows.
        new(RoleIds.SamaajAdmin, PermissionIds.NotificationsBroadcast),

        new(RoleIds.Member, PermissionIds.MembersRead),
        new(RoleIds.Member, PermissionIds.TimelinePost),

        // Every member may manage a family, because every member may create
        // one and become its head. Which family they may manage is decided in
        // member-family-service against the data - "are you the head of this
        // one?" - which is a stronger check than a role claim. Without this the
        // child endpoints were unreachable: nothing grants FamilyHead, so no
        // member could ever satisfy Family.Write.
        new(RoleIds.Member, PermissionIds.FamilyWrite),

        // And for the same reason, every member may lead a volunteer group.
        // A Samaaj admin creates the group and names its president, so a member
        // cannot make themselves one - but the president they name is usually an
        // ordinary member, and without this they could not decide their own
        // group's applications. Which group they may lead is checked against the
        // data in volunteer-groups-service ("are you this group's president?"),
        // which is a stronger check than a role claim.
        //
        // This is the third time this shape has bitten: nothing grants
        // FamilyHead, nothing grants VolunteerGroupPresident, and a permission
        // held only by an ungranted role is a permission nobody has. When adding
        // a permission, ask which *granted* role carries it.
        new(RoleIds.Member, PermissionIds.VolunteerGroupsLead),

        new(RoleIds.FamilyHead, PermissionIds.MembersRead),
        new(RoleIds.FamilyHead, PermissionIds.TimelinePost),
        new(RoleIds.FamilyHead, PermissionIds.FamilyWrite),

        new(RoleIds.VolunteerGroupPresident, PermissionIds.MembersRead),
        new(RoleIds.VolunteerGroupPresident, PermissionIds.TimelinePost),
        new(RoleIds.VolunteerGroupPresident, PermissionIds.VolunteerGroupsManage),
        new(RoleIds.VolunteerGroupPresident, PermissionIds.VolunteerGroupsLead),
        new(RoleIds.VolunteerGroupPresident, PermissionIds.EventsPublish),

        new(RoleIds.PathshalaTeacher, PermissionIds.MembersRead),
        new(RoleIds.PathshalaTeacher, PermissionIds.PathshalaAttendanceWrite),
        new(RoleIds.PathshalaTeacher, PermissionIds.PathshalaExamsWrite),

        new(RoleIds.PathshalaStudent, PermissionIds.MembersRead),

        new(RoleIds.ContentModerator, PermissionIds.MembersRead),
        new(RoleIds.ContentModerator, PermissionIds.TimelineModerate),
        new(RoleIds.ContentModerator, PermissionIds.SocialIssuesApprove),

        new(RoleIds.BoliManager, PermissionIds.MembersRead),
        new(RoleIds.BoliManager, PermissionIds.BoliManage),
        new(RoleIds.BoliManager, PermissionIds.BoliPublishResults),
    ];

    /// <summary>
    /// The roles a Samaaj administrator may hand out.
    /// </summary>
    /// <remarks>
    /// Not every role. Three kinds are missing, for three different reasons.
    ///
    /// <b>SuperAdmin</b> is platform administration, not Samaaj
    /// administration. Its only route is the bootstrap on an empty database, so
    /// that granting it can never be one compromised Samaaj Admin account away.
    ///
    /// <b>Member</b> and <b>FamilyHead</b> are earned, not granted - by
    /// registering, and by creating a household. Handing someone Member from
    /// this screen would write a grant with no account behind it.
    ///
    /// <b>PathshalaStudent</b> was described here as "created by enrolment",
    /// and nothing creates it: enrolment happens in pathshala-service, which
    /// cannot write role grants here. It is a label on the matrix, not a
    /// working grant, and nothing gates on it - pathshala-service decides who
    /// may read a child's records against its own enrolment rows instead
    /// (whose child is this, who teaches the class), which is a stronger check
    /// than a role claim and needs no cross-service write. Leave it unassignable
    /// and ungranted; do not gate anything new on it without first giving
    /// something the ability to grant it.
    ///
    /// This list is what both the role matrix and the assign-role command read,
    /// so a role can never be assignable in one and not the other.
    /// </remarks>
    public static IReadOnlyList<Guid> AdminAssignableRoleIds { get; } =
    [
        RoleIds.SamaajAdmin,
        RoleIds.ContentModerator,
        RoleIds.VolunteerGroupPresident,
        RoleIds.PathshalaTeacher,
        RoleIds.BoliManager,
    ];

    public static bool IsAdminAssignable(Guid roleId) => AdminAssignableRoleIds.Contains(roleId);

    /// <summary>The role with this name, or null when nothing is called that.</summary>
    public static Role? FindRoleByName(string? name) =>
        name is null ? null : Roles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
}
