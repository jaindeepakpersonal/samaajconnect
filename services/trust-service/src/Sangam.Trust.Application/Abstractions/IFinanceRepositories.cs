using Sangam.Trust.Domain.Donations;
using Sangam.Trust.Domain.Expenses;

namespace Sangam.Trust.Application.Abstractions;

// Collection is referenced as `Domain.Collections.Collection`, never bare -
// see that type's own remarks for why.

public interface IDonationRepository
{
    /// <summary>Tenant-filtered.</summary>
    Task<Donation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Donation>> ListByTrustAsync(
        Guid trustId, DonationStatus? status, CancellationToken cancellationToken = default);

    void Add(Donation donation);

    /// <summary>
    /// Reserves the next receipt number for this Trust, safe under concurrent
    /// collection requests.
    /// </summary>
    /// <remarks>
    /// Takes a Postgres transaction-scoped advisory lock keyed on the Trust id
    /// (<c>pg_advisory_xact_lock</c>, released automatically when the
    /// request's own transaction commits or rolls back - <c>TransactionBehavior</c>
    /// has already opened one), then reads <c>MAX(ReceiptNumber) + 1</c> for
    /// that Trust. Two collection requests for the same Trust racing each
    /// other queue on the lock rather than both computing the same number -
    /// the identical "serialise the contended counter" shape
    /// <c>boli-service</c>'s own row lock uses for a Boli's highest bid, sized
    /// down to an advisory lock because there is no natural row to lock here:
    /// a Trust's receipt sequence is a fact about its donations, not about the
    /// Trust row itself.
    /// </remarks>
    Task<int> ReserveNextReceiptNumberAsync(Guid trustId, CancellationToken cancellationToken = default);
}

public interface IExpenseRepository
{
    /// <summary>Tenant-filtered.</summary>
    Task<Expense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Expense>> ListByTrustAsync(
        Guid trustId, ExpenseStatus? status, CancellationToken cancellationToken = default);

    void Add(Expense expense);
}

public interface ICollectionRepository
{
    /// <summary>Tenant-filtered.</summary>
    Task<Domain.Collections.Collection?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Domain.Collections.Collection>> ListByTrustAsync(
        Guid trustId, Domain.Collections.CollectionStatus? status, CancellationToken cancellationToken = default);

    Task<Domain.Collections.Collection?> FindBySourceAsync(
        Domain.Collections.CollectionSourceType sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Tenant-filtered. Still-due Collections this member owes, oldest first.</summary>
    Task<IReadOnlyList<Domain.Collections.Collection>> ListDueForPayerAsync(
        Guid payerMemberId, CancellationToken cancellationToken = default);

    /// <summary>Tenant-filtered and tracked, for keeping a pledge and its Collection in step.</summary>
    Task<Domain.Collections.Collection?> GetBySourceAsync(
        Domain.Collections.CollectionSourceType sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    void Add(Domain.Collections.Collection collection);

    /// <summary>
    /// Inserts on its own scope, returning false when the unique index on
    /// (SourceType, SourceId) refuses a second Collection for a source
    /// already opened. This is what makes consuming
    /// <c>boli.result.published.v1</c> safe under at-least-once redelivery -
    /// the same "the index is the guarantee, the read beforehand is a
    /// courtesy" shape <c>ITrustRepository.TryAddAsync</c> already uses.
    /// </summary>
    Task<bool> TryOpenAsync(Domain.Collections.Collection collection, CancellationToken cancellationToken = default);
}
