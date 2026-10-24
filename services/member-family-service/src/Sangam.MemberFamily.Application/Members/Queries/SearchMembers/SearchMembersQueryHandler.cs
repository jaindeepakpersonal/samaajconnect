using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Members.Queries.SearchMembers;

public sealed class SearchMembersQueryHandler(
    IMemberProfileRepository profiles,
    IFamilyRepository families,
    ICurrentUser currentUser,
    IAreaScope areaScope)
    : IRequestHandler<SearchMembersQuery, Result<PagedResult<MemberResponse>>>
{
    public async Task<Result<PagedResult<MemberResponse>>> Handle(
        SearchMembersQuery query,
        CancellationToken cancellationToken)
    {
        // An Area Admin gets the administrator's view - unlisted members, every
        // privacy level seen through - only for a search confined to their own
        // locality, which is what the admin panel's Members screen sends for
        // them. Any other search is the member view, the directory they have
        // as a member of the Samaaj like anybody else (IAreaScope's remarks).
        var isAdmin = currentUser.IsInRole(Roles.SamaajAdmin)
            || currentUser.IsInRole(Roles.SuperAdmin)
            || areaScope.AdministersLocality(query.LocalityId);

        var viewer = new ProfileViewer(currentUser.UserId, isAdmin);

        // One row more than asked for, so HasMore is known without a second
        // COUNT(*) query - the extra row is sliced off below and never reaches
        // the caller.
        var found = await profiles.SearchAsync(
            new MemberSearchFilter(query.Term, query.LocalityId, query.GautraId, query.ProfessionId),
            viewer,
            query.Skip,
            query.Take + 1,
            // Administrators see members who have taken themselves out of the
            // directory; nobody else does. The same role decides whether the
            // privacy levels are seen through, and for the same reason:
            // correcting a member's details is administrative work.
            includeUnlisted: isAdmin,
            cancellationToken);

        var hasMore = found.Count > query.Take;
        var page = found.Take(query.Take).ToList();

        var parents = await ParentNamesAsync(page, isAdmin, cancellationToken);

        IReadOnlyList<MemberResponse> results = page
            .Select(profile =>
            {
                var row = profile.ToDirectoryResponse(viewer);

                return parents.TryGetValue(profile.Id, out var named)
                    ? row with { FatherName = named.Father, MotherName = named.Mother }
                    : row;
            })
            .ToList();

        return Result.Success(new PagedResult<MemberResponse>(results, hasMore));
    }

    /// <summary>
    /// Father and mother for each member on the page, from their households -
    /// three queries for the whole page, never one per row.
    /// </summary>
    /// <remarks>
    /// A parent appears by name only where the viewer could find them in the
    /// directory anyway: listed, or the viewer is an administrator. Someone who
    /// took themselves out of the search should not reappear on a relative's
    /// card - and that also keeps every erased profile out, since erasure
    /// unlists. Which parent is the father is the profile's gender; one whose
    /// gender is not recorded is left off rather than guessed into a line.
    /// </remarks>
    private async Task<Dictionary<Guid, (string? Father, string? Mother)>> ParentNamesAsync(
        IReadOnlyList<MemberProfile> page, bool isAdmin, CancellationToken cancellationToken)
    {
        var memberIds = page.Select(profile => profile.Id).ToList();
        var households = await families.ListForMembersAsync(memberIds, cancellationToken);

        var parentIdsByMember = new Dictionary<Guid, IReadOnlyList<Guid>>();

        foreach (var memberId in memberIds)
        {
            var household = households.FirstOrDefault(family => family.FindMember(memberId) is not null);

            if (household?.ParentsOf(memberId) is { Count: > 0 } parentIds)
            {
                parentIdsByMember[memberId] = parentIds;
            }
        }

        var parentProfiles = (await profiles.ListByIdsAsync(
                [.. parentIdsByMember.Values.SelectMany(ids => ids).Distinct()], cancellationToken))
            .Where(parent => isAdmin || parent.IsListedInDirectory)
            .ToDictionary(parent => parent.Id);

        var named = new Dictionary<Guid, (string? Father, string? Mother)>();

        foreach (var (memberId, parentIds) in parentIdsByMember)
        {
            var known = parentIds
                .Select(id => parentProfiles.GetValueOrDefault(id))
                .OfType<MemberProfile>()
                .OrderBy(parent => parent.FullName)
                .ToList();

            var father = known.FirstOrDefault(parent => parent.Gender == Gender.Male)?.FullName;
            var mother = known.FirstOrDefault(parent => parent.Gender == Gender.Female)?.FullName;

            if (father is not null || mother is not null)
            {
                named[memberId] = (father, mother);
            }
        }

        return named;
    }
}
