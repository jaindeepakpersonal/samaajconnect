using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Members.Queries.GetMember;

/// <summary>
/// One member's profile, as this viewer is allowed to see it.
/// </summary>
/// <remarks>
/// API-CONTRACTS.md has promised `GET /v1/members/{id}` since the contract was
/// written and nothing implemented it; the member portal's directory screen is
/// what noticed. A directory whose rows cannot be opened is a list, not a
/// directory.
///
/// It goes through <c>ToDirectoryResponse</c>, the same mapper the search uses,
/// rather than a second one. That mapper is the single place the per-field
/// privacy rules are applied, and a detail view is exactly where a second copy
/// of them would drift into showing more than the list does.
/// </remarks>
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
public sealed record GetMemberQuery(Guid MemberId) : IQuery<MemberResponse>;

public sealed class GetMemberQueryHandler(
    IMemberProfileRepository profiles,
    ICurrentUser currentUser)
    : IRequestHandler<GetMemberQuery, Result<MemberResponse>>
{
    public async Task<Result<MemberResponse>> Handle(
        GetMemberQuery query,
        CancellationToken cancellationToken)
    {
        // Tenant-filtered by the global query filter, so a member of another
        // Samaaj is simply not found rather than forbidden.
        var profile = await profiles.GetByIdAsync(query.MemberId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<MemberResponse>(
                Error.NotFound("Member.NotFound", "No such member in this Samaaj."));
        }

        var viewer = new ProfileViewer(
            currentUser.UserId,
            currentUser.IsInRole(Roles.SamaajAdmin) || currentUser.IsInRole(Roles.SuperAdmin));

        return Result.Success(profile.ToDirectoryResponse(viewer));
    }
}
