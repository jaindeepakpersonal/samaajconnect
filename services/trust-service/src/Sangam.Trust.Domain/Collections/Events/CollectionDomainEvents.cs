using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Collections;

/// <summary>
/// A payment became due against a Trust - a donation pledge, or a Boli result
/// this Trust's locality won.
/// </summary>
public sealed record CollectionDueDomainEvent(
    Guid CollectionId,
    Guid TenantId,
    Guid TrustId,
    string SourceType,
    Guid SourceId,
    Guid? PayerMemberId,
    long Amount,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.collection.due.v1";
}

/// <summary>A due payment was actually collected.</summary>
public sealed record CollectionSettledDomainEvent(
    Guid CollectionId,
    Guid TenantId,
    Guid TrustId,
    long Amount,
    string Mode,
    Guid CollectedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.collection.settled.v1";
}
