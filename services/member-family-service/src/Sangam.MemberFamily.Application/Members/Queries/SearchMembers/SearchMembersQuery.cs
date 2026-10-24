using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Members.Queries.SearchMembers;

/// <summary>The Samaaj member directory, privacy-filtered per field.</summary>
[RequiresRoles(
    Roles.Member,
    Roles.FamilyHead,
    Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher,
    Roles.PathshalaStudent,
    Roles.ContentModerator,
    Roles.BoliManager,
    Roles.SamaajAdmin,
    Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record SearchMembersQuery(
    string? Term,
    Guid? LocalityId,
    int Skip = 0,
    int Take = 20,
    Guid? GautraId = null,
    Guid? ProfessionId = null)
    : IQuery<PagedResult<MemberResponse>>;
