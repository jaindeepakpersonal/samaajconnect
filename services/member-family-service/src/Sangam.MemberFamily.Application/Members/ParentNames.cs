using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Members;

/// <summary>
/// Father and mother for a set of members, from their households - the
/// directory card's and the member page's Father / Mother lines.
/// </summary>
/// <remarks>
/// Three queries for any number of members, never one per member. A parent
/// appears by name only where the viewer could find them in the directory
/// anyway: listed, or the viewer is an administrator. Someone who took
/// themselves out of the search should not reappear on a relative's card -
/// and that also keeps every erased profile out, since erasure unlists. Which
/// parent is the father is the profile's gender; one whose gender is not
/// recorded is left off rather than guessed into a line.
/// </remarks>
internal static class ParentNames
{
    public static async Task<Dictionary<Guid, (string? Father, string? Mother)>> ResolveAsync(
        IMemberProfileRepository profiles,
        IFamilyRepository families,
        IReadOnlyCollection<Guid> memberIds,
        bool viewerIsAdmin,
        CancellationToken cancellationToken)
    {
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
            .Where(parent => viewerIsAdmin || parent.IsListedInDirectory)
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
