using FluentValidation;
using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Families.Queries.ListFamilies;

/// <summary>
/// This Samaaj's households, newest first, each with its members and its minor
/// children - the admin panel's Family &amp; Children screen, optionally narrowed
/// to one locality.
/// </summary>
/// <remarks>
/// <para>
/// <b>A household is in a locality when any of its active members lives
/// there</b> (their permanent address), so a family split across two
/// localities is found from either. A family has no locality of its own; its
/// members' addresses are the only place one is recorded.
/// </para>
/// <para>
/// <b>Administrators only, and an Area Admin only for their own locality.</b>
/// This reads past every member's directory privacy - it lists unlisted members
/// and children by name - which is administrative work, the same line
/// <c>CorrectMemberDetailsCommand</c> draws. An Area Admin asking for any other
/// locality, or for the whole Samaaj, is refused rather than quietly narrowed:
/// the screen fixes their filter, so a different request is not one it sends.
/// </para>
/// <para>
/// <b>A child's age, never their date of birth.</b> An administrator scanning
/// households needs to know a child is eleven; the birthday is on the child's
/// own record, which only their household reads.
/// </para>
/// </remarks>
[RequiresRoles(Roles.SamaajAdmin, Roles.SuperAdmin, Roles.AreaAdmin)]
[RequiresPermission(PermissionKeys.MembersWrite)]
public sealed record ListFamiliesQuery(Guid? LocalityId, int Skip = 0, int Take = 20)
    : IQuery<PagedResult<FamilyOverviewResponse>>;

/// <summary>One household as the admin list shows it.</summary>
public sealed record FamilyOverviewResponse(
    Guid Id,
    string FamilyCode,
    Guid? HeadMemberId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<FamilyOverviewMember> Members,
    IReadOnlyList<FamilyOverviewChild> Children);

/// <summary>An active member of the household. <c>LocalityId</c> is their permanent address's.</summary>
public sealed record FamilyOverviewMember(
    Guid MemberId,
    string FullName,
    string Relationship,
    bool IsHead,
    Guid? LocalityId);

/// <summary>A minor child's record - converted children are members now, and listed as members.</summary>
public sealed record FamilyOverviewChild(Guid Id, string FullName, int Age, string Gender);

public sealed class ListFamiliesQueryValidator : AbstractValidator<ListFamiliesQuery>
{
    public ListFamiliesQueryValidator()
    {
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 100);
    }
}

public sealed class ListFamiliesQueryHandler(
    IFamilyDirectory directory,
    IAreaScope areaScope,
    IDateTimeProvider clock)
    : IRequestHandler<ListFamiliesQuery, Result<PagedResult<FamilyOverviewResponse>>>
{
    public async Task<Result<PagedResult<FamilyOverviewResponse>>> Handle(
        ListFamiliesQuery query, CancellationToken cancellationToken)
    {
        if (areaScope.AreaLocalityId is { } own && query.LocalityId != own)
        {
            return Result.Failure<PagedResult<FamilyOverviewResponse>>(Error.Forbidden(
                "Families.OutsideArea", "An Area Admin sees the households of their own locality."));
        }

        // One row more than asked for, so HasMore is known without a COUNT(*).
        var found = await directory.ListAsync(
            query.LocalityId, query.Skip, query.Take + 1, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), cancellationToken);

        return Result.Success(new PagedResult<FamilyOverviewResponse>(
            [.. found.Take(query.Take)], found.Count > query.Take));
    }
}
