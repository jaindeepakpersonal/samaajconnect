using Sangam.Trust.Domain.Members;

namespace Sangam.Trust.Application.Members;

// Trust is referenced as `Domain.Trusts.Trust`, never bare - see
// ITrustRepository's own remarks.

internal static class TrustMemberMappings
{
    /// <summary>The Trust supplies the role's title; the roster row holds only its id.</summary>
    public static TrustMemberResponse ToResponse(this TrustMember member, Domain.Trusts.Trust trust) => new(
        member.Id,
        member.TrustId,
        member.MemberId,
        trust.FindRole(member.RoleId)?.Title,
        member.Status.ToString(),
        member.AddedBy,
        member.AddedAt,
        member.RemovedBy,
        member.RemovedAt,
        member.RoleId);
}
