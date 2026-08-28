using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Children;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Families;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Members.Queries.GetMyData;

/// <summary>
/// Everything member-family-service holds about the caller and their household.
/// </summary>
/// <remarks>
/// DPDP section 11. Per-service by design - see docs/product/DPDP-COMPLIANCE.md
/// and the matching query in identity-tenant-service. Children are included
/// because the household's records are held on the strength of *this* member's
/// parental consent, so they are part of what they are entitled to see.
/// </remarks>
[RequiresRoles(
    Roles.Member,
    Roles.FamilyHead,
    Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher,
    Roles.PathshalaStudent,
    Roles.ContentModerator,
    Roles.SamaajAdmin,
    Roles.SuperAdmin)]
public sealed record GetMyDataQuery : IQuery<MyMemberDataResponse>;

public sealed record MyMemberDataResponse(
    string ExportedAt,
    string Service,
    MyProfileResponse? Profile,
    FamilyResponse? Family,
    IReadOnlyList<ChildResponse> Children,
    IReadOnlyList<string> ProcessingPurposes,
    IReadOnlyList<string> HeldElsewhere);

public sealed class GetMyDataQueryHandler(
    IMemberProfileRepository profiles,
    IFamilyRepository families,
    IChildRepository children,
    IChildConversionRepository conversions,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetMyDataQuery, Result<MyMemberDataResponse>>
{
    public async Task<Result<MyMemberDataResponse>> Handle(
        GetMyDataQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<MyMemberDataResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var profile = await profiles.GetByIdAsync(memberId, cancellationToken);
        var family = await families.GetForMemberAsync(memberId, cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        IReadOnlyList<ChildResponse> childResponses = [];
        FamilyResponse? familyResponse = null;

        if (family is not null)
        {
            var householdProfiles = await profiles.SearchAsync(null, null, 200, cancellationToken);
            familyResponse = family.ToResponse(memberId, householdProfiles);

            var pending = await conversions.ListPendingAsync(cancellationToken);
            var pendingChildIds = pending.Select(request => request.ChildProfileId).ToHashSet();

            childResponses = (await children.ListForFamilyAsync(family.Id, cancellationToken))
                .Select(child => child.ToResponse(today, pendingChildIds.Contains(child.Id)))
                .ToList();
        }

        // The processing activities half of section 11, in plain words rather
        // than a list of database columns.
        return Result.Success(new MyMemberDataResponse(
            clock.UtcNow.ToString("O"),
            "member-family-service",
            profile?.ToOwnerResponse(),
            familyResponse,
            childResponses,
            [
                "Your profile is shown in your Samaaj's member directory, field by field, "
                + "according to the privacy settings you chose.",
                "Your family links let your household be managed as one, and let a family "
                + "head add and manage children's records.",
                "Children's records are held on your recorded parental consent, and are "
                + "visible to your family and to your Samaaj's administrators only.",
            ],
            [
                "identity-tenant-service: your login, roles and consent history",
                "audit-notification-service: your notifications, and the audit record of actions taken",
            ]));
    }
}
