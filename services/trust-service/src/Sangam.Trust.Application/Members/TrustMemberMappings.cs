using Sangam.Trust.Domain.Members;

namespace Sangam.Trust.Application.Members;

internal static class TrustMemberMappings
{
    public static TrustMemberResponse ToResponse(this TrustMember member) => new(
        member.Id,
        member.TrustId,
        member.MemberId,
        member.PositionTitle,
        member.Status.ToString(),
        member.AddedBy,
        member.AddedAt,
        member.RemovedBy,
        member.RemovedAt);
}
