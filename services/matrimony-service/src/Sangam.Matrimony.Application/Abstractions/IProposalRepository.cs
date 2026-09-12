using Sangam.Matrimony.Domain.Proposals;

namespace Sangam.Matrimony.Application.Abstractions;

public interface IProposalRepository
{
    Task<Proposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a Pending or Accepted proposal already exists between the
    /// two members, in either direction - the same "a member belongs to one
    /// household, and a pending request counts" rule
    /// `RequestJoinFamilyCommand` already applies, adapted here to stop
    /// duplicate/overlapping proposals rather than duplicate households.
    /// </summary>
    Task<bool> HasActiveBetweenAsync(Guid memberIdA, Guid memberIdB, CancellationToken cancellationToken = default);

    /// <summary>Sent by this member, most recent first.</summary>
    Task<IReadOnlyList<Proposal>> ListSentAsync(Guid fromMemberId, CancellationToken cancellationToken = default);

    /// <summary>Received by this member, most recent first.</summary>
    Task<IReadOnlyList<Proposal>> ListReceivedAsync(Guid toMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every proposal this member sent or received, in any Samaaj query
    /// filter's blind spot - for the account-erasure consumer, the same
    /// "no request, no resolved tenant" shape as
    /// <see cref="IBiodataRepository.GetForConsumerAsync"/>.
    /// </summary>
    Task<IReadOnlyList<Proposal>> ListForConsumerAsync(Guid memberId, CancellationToken cancellationToken = default);

    void Add(Proposal proposal);
}
