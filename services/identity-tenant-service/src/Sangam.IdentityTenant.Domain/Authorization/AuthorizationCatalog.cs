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
        new(RoleIds.SamaajAdmin, PermissionIds.EventsPublish),
        new(RoleIds.SamaajAdmin, PermissionIds.SocialIssuesApprove),
        new(RoleIds.SamaajAdmin, PermissionIds.CelebrityVotingConfigure),
        new(RoleIds.SamaajAdmin, PermissionIds.BoliManage),
        new(RoleIds.SamaajAdmin, PermissionIds.BoliPublishResults),
        new(RoleIds.SamaajAdmin, PermissionIds.AuditRead),

        new(RoleIds.Member, PermissionIds.MembersRead),
        new(RoleIds.Member, PermissionIds.TimelinePost),

        new(RoleIds.FamilyHead, PermissionIds.MembersRead),
        new(RoleIds.FamilyHead, PermissionIds.TimelinePost),
        new(RoleIds.FamilyHead, PermissionIds.FamilyWrite),

        new(RoleIds.VolunteerGroupPresident, PermissionIds.MembersRead),
        new(RoleIds.VolunteerGroupPresident, PermissionIds.TimelinePost),
        new(RoleIds.VolunteerGroupPresident, PermissionIds.VolunteerGroupsManage),
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
}
