using Sangam.MemberFamily.Domain.Families;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Families;

public static class FamilyMappings
{
    /// <summary>
    /// Builds the family view for one viewer. Names come from the profiles
    /// passed in rather than a join inside the aggregate, so the family
    /// aggregate stays unaware of profiles.
    /// </summary>
    public static FamilyResponse ToResponse(
        this Family family,
        Guid viewerMemberId,
        IReadOnlyCollection<MemberProfile> profiles)
    {
        var isHead = family.IsHead(viewerMemberId);
        var names = profiles.ToDictionary(p => p.Id, p => p.FullName);

        var members = family.Members
            .OrderBy(m => m.Status)
            .ThenBy(m => m.RequestedAt)
            .Select(member => new FamilyMemberResponse(
                member.Id,
                member.MemberProfileId,
                names.GetValueOrDefault(member.MemberProfileId, "Unknown member"),
                member.Relationship.ToString(),
                member.Status.ToString(),
                member.RequestedAt,
                member.DecidedAt))
            .ToList();

        return new FamilyResponse(
            family.Id,
            family.FamilyHeadMemberId,
            // Only the head sees the code: it is the token for joining, so
            // sharing it with everyone would let any member invite anyone.
            isHead ? family.FamilyCode : null,
            isHead,
            members,
            family.CreatedAt);
    }
}
