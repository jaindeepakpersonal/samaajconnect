using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Members;

/// <summary>
/// A member joined a Trust's roster, or rejoined it after being removed.
/// Raised on both the first add and every reactivation.
/// </summary>
public sealed record TrustMemberAddedDomainEvent(
    Guid TrustMemberId,
    Guid TenantId,
    Guid TrustId,
    Guid MemberId,
    string? PositionTitle,
    Guid AddedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.member.added.v1";
}

/// <summary>A trustee's position title changed.</summary>
public sealed record TrustMemberUpdatedDomainEvent(
    Guid TrustMemberId,
    Guid TenantId,
    Guid TrustId,
    Guid MemberId,
    string? PositionTitle,
    Guid UpdatedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.member.updated.v1";
}

/// <summary>A member was taken off a Trust's roster.</summary>
public sealed record TrustMemberRemovedDomainEvent(
    Guid TrustMemberId,
    Guid TenantId,
    Guid TrustId,
    Guid MemberId,
    Guid RemovedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.member.removed.v1";
}
