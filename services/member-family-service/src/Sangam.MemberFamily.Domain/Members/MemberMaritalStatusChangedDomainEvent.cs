using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Members;

/// <summary>
/// A member's marital status changed - and with it, whether they may keep a
/// matrimony biodata. matrimony-service consumes it.
/// </summary>
/// <remarks>
/// It carries the one fact the consumer needs, <see cref="EligibleForMatrimony"/>,
/// and not the status itself. The audit service keeps every payload verbatim and
/// forever, and "this member is divorced" is personal data a payload has no need
/// to hold; "may, or may not, keep a biodata" is what the rule acts on. The
/// status stays on the profile, where the member can change it and erasure can
/// reach it.
/// </remarks>
public sealed record MemberMaritalStatusChangedDomainEvent(
    Guid MemberId,
    Guid TenantId,
    bool EligibleForMatrimony,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "members.marital-status.changed.v1";
}
