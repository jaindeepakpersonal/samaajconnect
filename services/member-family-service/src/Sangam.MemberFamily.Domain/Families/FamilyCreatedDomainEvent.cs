using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Families;

public sealed record FamilyCreatedDomainEvent(
    Guid FamilyId,
    Guid TenantId,
    Guid FamilyHeadMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "members.family.created.v1";
}
