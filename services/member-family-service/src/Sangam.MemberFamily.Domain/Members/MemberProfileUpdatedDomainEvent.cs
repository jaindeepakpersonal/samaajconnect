using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Members;

public sealed record MemberProfileUpdatedDomainEvent(
    Guid MemberId,
    Guid TenantId,
    string FullName,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "members.profile.updated.v1";
}
