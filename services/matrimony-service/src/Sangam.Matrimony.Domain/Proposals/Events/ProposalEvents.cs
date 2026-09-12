using Sangam.Matrimony.Domain.Common;

namespace Sangam.Matrimony.Domain.Proposals.Events;

public sealed record ProposalSentDomainEvent(
    Guid ProposalId,
    Guid TenantId,
    Guid FromMemberId,
    Guid ToMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "matrimony.proposal.sent.v1";
}

/// <summary>Carries the outcome - "Accepted" or "Declined" - as a string, the
/// same convention `MemberProfileUpdatedDomainEvent` uses for enum-shaped
/// facts on the wire.</summary>
public sealed record ProposalRespondedDomainEvent(
    Guid ProposalId,
    Guid TenantId,
    Guid FromMemberId,
    Guid ToMemberId,
    string Outcome,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "matrimony.proposal.responded.v1";
}

public sealed record ProposalWithdrawnDomainEvent(
    Guid ProposalId,
    Guid TenantId,
    Guid FromMemberId,
    Guid ToMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "matrimony.proposal.withdrawn.v1";
}
