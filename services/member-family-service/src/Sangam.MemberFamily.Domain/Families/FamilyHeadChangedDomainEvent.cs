using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Families;

/// <summary>
/// The household's head - its eldest member, as the family admin sees it - was
/// named or cleared. Carries ids only, never a name.
/// </summary>
public sealed record FamilyHeadChangedDomainEvent(
    Guid FamilyId,
    Guid TenantId,
    Guid? FamilyHeadMemberId,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "members.family.head-changed.v1";
}
