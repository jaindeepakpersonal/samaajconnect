using Sangam.Boli.Domain.Auctions;

namespace Sangam.Boli.Application.Abstractions;

/// <summary>Occasions and the Boli types under them.</summary>
public interface IOccasionRepository
{
    Task<BoliOccasion?> GetByIdAsync(Guid occasionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BoliOccasion>> ListAsync(CancellationToken cancellationToken = default);

    void Add(BoliOccasion occasion);
}

/// <summary>
/// Boli, bids and results.
/// </summary>
/// <remarks>
/// The bidding path is the reason this interface is shaped the way it is. See
/// <see cref="LockForBiddingAsync"/>.
/// </remarks>
public interface IBoliRepository
{
    Task<Domain.Auctions.Boli?> GetByIdAsync(Guid boliId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Domain.Auctions.Boli>> ListForOccasionAsync(
        Guid occasionId, CancellationToken cancellationToken = default);

    /// <summary>Every Boli currently taking bids, across occasions.</summary>
    Task<IReadOnlyList<Domain.Auctions.Boli>> ListOpenAsync(
        CancellationToken cancellationToken = default);

    void Add(Domain.Auctions.Boli boli);

    /// <summary>
    /// Loads a Boli and holds a row lock on it until the request's transaction
    /// commits.
    /// </summary>
    /// <remarks>
    /// <b>This is what makes bidding correct.</b> Placing a bid is a
    /// check-then-insert — read the highest, decide whether the new amount clears
    /// it, write — and two bidders who read before either writes both pass the
    /// check. In the last minute of a Boli that is the normal case, not the edge.
    ///
    /// <c>SELECT ... FOR UPDATE</c> on the Boli row serialises them. Bids on one
    /// Boli are strictly ordered, which is the domain rather than a limitation:
    /// two people cannot both be the highest bidder. Bids on *different* Boli do
    /// not contend at all, which is what keeps the lock cheap even when a Samaaj
    /// is running twenty of them at once.
    ///
    /// The lock is taken inside the request's transaction — <c>TransactionBehavior</c>
    /// has already opened one for the command — and released when it commits.
    /// That is deliberately the opposite of what celebrity-voting-service does
    /// with a vote, where the write is pushed onto its own scope precisely to
    /// avoid serialising voters. Here the serialisation is the point.
    /// </remarks>
    Task<Domain.Auctions.Boli?> LockForBiddingAsync(
        Guid boliId, CancellationToken cancellationToken = default);

    /// <summary>The highest amount bid so far, or null when nobody has bid.</summary>
    Task<long?> HighestAmountAsync(Guid boliId, CancellationToken cancellationToken = default);

    /// <summary>The highest bid itself, for recording a result.</summary>
    Task<Bid?> HighestBidAsync(Guid boliId, CancellationToken cancellationToken = default);

    /// <summary>Bids on one Boli, highest first.</summary>
    Task<IReadOnlyList<Bid>> ListBidsAsync(Guid boliId, CancellationToken cancellationToken = default);

    void AddBid(Bid bid);

    Task<BoliResult?> GetResultAsync(Guid boliId, CancellationToken cancellationToken = default);

    /// <summary>Published results across the Samaaj, newest first.</summary>
    Task<IReadOnlyList<BoliResult>> ListPublishedResultsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Results recorded and not yet announced, oldest first.
    /// </summary>
    /// <remarks>
    /// <b>The queue the two-step workflow implies and nothing could answer.</b>
    /// Recording and publishing are deliberately separate acts, which means a
    /// result can sit between them - and until now the only way to find one was
    /// to already know its Boli id and ask for that Boli's result. A workflow
    /// whose middle state is unlistable is a workflow that quietly loses things:
    /// a Boli closed and recorded on the day is announced only if somebody
    /// remembers it.
    ///
    /// Oldest first, unlike the published list. This is a work queue, and the
    /// one that has been waiting longest is the one most likely to have been
    /// forgotten.
    /// </remarks>
    Task<IReadOnlyList<BoliResult>> ListUnpublishedResultsAsync(
        CancellationToken cancellationToken = default);

    void AddResult(BoliResult result);
}
