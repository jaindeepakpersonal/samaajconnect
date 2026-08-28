using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Members;

/// <summary>
/// A member profile was corrected or completed.
/// </summary>
/// <param name="ChangedFields">
/// The names of the fields that changed, never their values. This is the
/// before-state SECURITY-CHECKLIST.md asks for on a correction; values would
/// put a member's previous mobile number and address into an append-only audit
/// table, which is the one place the platform tries hardest not to leave
/// personal data.
/// </param>
/// <param name="UpdatedBy">
/// Who made the change - the member themselves, or a Samaaj admin correcting
/// their details. That distinction is the whole reason this event is audited.
/// </param>
public sealed record MemberProfileUpdatedDomainEvent(
    Guid MemberId,
    Guid TenantId,
    string FullName,
    IReadOnlyCollection<string> ChangedFields,
    Guid UpdatedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "members.profile.updated.v1";
}
