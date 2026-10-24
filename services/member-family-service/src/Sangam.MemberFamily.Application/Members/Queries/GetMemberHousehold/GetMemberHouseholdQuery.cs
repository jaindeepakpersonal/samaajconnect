using FluentValidation;
using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Families;

namespace Sangam.MemberFamily.Application.Members.Queries.GetMemberHousehold;

/// <summary>
/// The household a member belongs to, as their profile page shows it: who is
/// in it, who is its head and its admin, and how each is related to the admin.
/// </summary>
/// <remarks>
/// <para>
/// Who is in somebody's household is other members' data, so this answers with
/// less than the family screen does: only <b>active</b> members, only those the
/// viewer could find in the directory anyway (listed, or the viewer is an
/// administrator - the rule <c>ParentNames</c> already follows), and the member
/// whose page this is. Never the family code, never a pending request, and
/// <b>never a child's record</b>: a minor's name is held on a parent's consent
/// (DPDP s.9), for the household, not for the Samaaj's member pages.
/// </para>
/// <para>
/// No household answers with a null <c>Household</c>, not a 404 - "not in a
/// household on the platform" is an ordinary fact about a member, and a 404
/// would also be what an id from another Samaaj gets.
/// </para>
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
public sealed record GetMemberHouseholdQuery(Guid MemberId) : IQuery<MemberHouseholdResponse>;

public sealed class GetMemberHouseholdQueryValidator : AbstractValidator<GetMemberHouseholdQuery>
{
    public GetMemberHouseholdQueryValidator() => RuleFor(x => x.MemberId).NotEmpty();
}

public sealed class GetMemberHouseholdQueryHandler(
    IMemberProfileRepository profiles,
    IFamilyRepository families,
    ICurrentUser currentUser,
    IAreaScope areaScope)
    : IRequestHandler<GetMemberHouseholdQuery, Result<MemberHouseholdResponse>>
{
    public async Task<Result<MemberHouseholdResponse>> Handle(
        GetMemberHouseholdQuery query,
        CancellationToken cancellationToken)
    {
        // Tenant-filtered: a member of another Samaaj is not found.
        var member = await profiles.GetByIdAsync(query.MemberId, cancellationToken);

        if (member is null)
        {
            return Result.Failure<MemberHouseholdResponse>(
                Error.NotFound("Member.NotFound", "No such member in this Samaaj."));
        }

        var household = (await families.ListForMembersAsync([member.Id], cancellationToken)).FirstOrDefault();

        if (household is null)
        {
            return Result.Success(new MemberHouseholdResponse(null));
        }

        var isAdmin = currentUser.IsInRole(Roles.SamaajAdmin)
            || currentUser.IsInRole(Roles.SuperAdmin)
            || areaScope.AdministersLocality(member.PermanentAddress.LocalityId);

        var active = household.ActiveMembers();
        var people = (await profiles.ListByIdsAsync([.. active.Select(m => m.MemberProfileId)], cancellationToken))
            .ToDictionary(profile => profile.Id);

        IReadOnlyList<HouseholdPersonResponse> shown =
        [
            .. active
                .Where(m => people.TryGetValue(m.MemberProfileId, out var profile)
                    && (m.MemberProfileId == member.Id
                        || m.MemberProfileId == currentUser.UserId
                        || isAdmin
                        || profile.IsListedInDirectory))
                .Select(m =>
                {
                    var profile = people[m.MemberProfileId];

                    return new HouseholdPersonResponse(
                        profile.Id,
                        profile.FullName,
                        MemberMappings.PhotoPath(profile.Id, profile.PhotoImageId),
                        household.IsAdmin(m.MemberProfileId) ? null : m.Relationship.ToString(),
                        IsHead: household.FamilyHeadMemberId == m.MemberProfileId,
                        IsAdmin: household.IsAdmin(m.MemberProfileId));
                })
                .OrderByDescending(p => p.IsHead)
                .ThenByDescending(p => p.IsAdmin)
                .ThenBy(p => p.FullName),
        ];

        // Named after its head, or its admin while it has none - how people
        // refer to a household.
        var namedAfter = household.FamilyHeadMemberId ?? household.FamilyAdminMemberId;
        var namedAfterName = shown.FirstOrDefault(p => p.MemberId == namedAfter)?.FullName;

        return Result.Success(new MemberHouseholdResponse(
            new HouseholdResponse(household.Id, namedAfterName, shown)));
    }
}

/// <summary>Null <see cref="Household"/> when the member is not in one.</summary>
public sealed record MemberHouseholdResponse(HouseholdResponse? Household);

/// <summary>
/// <paramref name="NamedAfter"/> is the head's name, or the admin's while there
/// is no head - null when that person is not one the viewer may see.
/// </summary>
public sealed record HouseholdResponse(
    Guid FamilyId,
    string? NamedAfter,
    IReadOnlyList<HouseholdPersonResponse> Members);

/// <summary>
/// <paramref name="RelationshipToAdmin"/> is how this person joined - Spouse,
/// Parent, Child, Sibling, Other - relative to the household's admin, and null
/// for the admin themself.
/// </summary>
public sealed record HouseholdPersonResponse(
    Guid MemberId,
    string FullName,
    string? PhotoUrl,
    string? RelationshipToAdmin,
    bool IsHead,
    bool IsAdmin);
