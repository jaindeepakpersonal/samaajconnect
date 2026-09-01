using Microsoft.EntityFrameworkCore;
using Sangam.Boli.Application.Abstractions;
using Sangam.Boli.Domain.Auctions;
using Sangam.Boli.Infrastructure.Persistence;

namespace Sangam.Boli.Infrastructure.Repositories;

public sealed class OccasionRepository(BoliDbContext context) : IOccasionRepository
{
    public Task<BoliOccasion?> GetByIdAsync(
        Guid occasionId, CancellationToken cancellationToken = default) =>
        context.Occasions
            .Include(o => o.Types)
            .FirstOrDefaultAsync(o => o.Id == occasionId, cancellationToken);

    public async Task<IReadOnlyList<BoliOccasion>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await context.Occasions
            .Include(o => o.Types)
            .OrderByDescending(o => o.OccasionDate)
            .ThenByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(BoliOccasion occasion) => context.Occasions.Add(occasion);
}

public sealed class BoliRepository(BoliDbContext context) : IBoliRepository
{
    public Task<Domain.Auctions.Boli?> GetByIdAsync(
        Guid boliId, CancellationToken cancellationToken = default) =>
        context.Boli.FirstOrDefaultAsync(b => b.Id == boliId, cancellationToken);

    public async Task<IReadOnlyList<Domain.Auctions.Boli>> ListForOccasionAsync(
        Guid occasionId, CancellationToken cancellationToken = default) =>
        await context.Boli
            .Where(b => b.OccasionId == occasionId)
            .OrderBy(b => b.StartAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Domain.Auctions.Boli>> ListOpenAsync(
        CancellationToken cancellationToken = default) =>
        await context.Boli
            .Where(b => b.Status == BoliStatus.Open)
            .OrderBy(b => b.EndAt)
            .ToListAsync(cancellationToken);

    public void Add(Domain.Auctions.Boli boli) => context.Boli.Add(boli);

    /// <summary>
    /// Takes a row lock on the Boli, then loads it.
    /// </summary>
    /// <remarks>
    /// See <see cref="IBoliRepository.LockForBiddingAsync"/> for why the lock is
    /// what makes bidding correct. Three details matter about how:
    ///
    /// The lock is taken with its own statement rather than by loading through
    /// <c>FromSql</c>. EF has no first-class row-lock hint, and a
    /// <c>FromSqlInterpolated</c> that anything is composed on top of is wrapped
    /// in a subquery — at which point the <c>FOR UPDATE</c> either becomes a
    /// syntax error or silently stops applying to the rows you get back. Issuing
    /// the lock and then loading normally is the same two round trips and cannot
    /// be quietly undone by a later <c>.Where()</c>.
    ///
    /// The lock is scoped to the request's transaction, which
    /// <c>TransactionBehavior</c> has already opened for a command, and is
    /// released when that commits or rolls back.
    ///
    /// The load deliberately ignores the tenant query filter, which is exactly
    /// why every caller re-checks <c>TenantId</c> itself — the IDOR guard in root
    /// CLAUDE.md section 6 requires that on a write path regardless.
    /// </remarks>
    public async Task<Domain.Auctions.Boli?> LockForBiddingAsync(
        Guid boliId, CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM boli WHERE id = {boliId} FOR UPDATE", cancellationToken);

        return await context.Boli
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == boliId, cancellationToken);
    }

    public async Task<long?> HighestAmountAsync(
        Guid boliId, CancellationToken cancellationToken = default)
    {
        // MAX in the database, not a scan of a loaded collection - see Boli's
        // remarks about why bids are not part of that aggregate.
        var amounts = await context.Bids
            .Where(b => b.BoliId == boliId)
            .Select(b => (long?)b.Amount)
            .ToListAsync(cancellationToken);

        return amounts.Count == 0 ? null : amounts.Max();
    }

    public Task<Bid?> HighestBidAsync(Guid boliId, CancellationToken cancellationToken = default) =>
        context.Bids
            .Where(b => b.BoliId == boliId)
            .OrderByDescending(b => b.Amount)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Bid>> ListBidsAsync(
        Guid boliId, CancellationToken cancellationToken = default) =>
        await context.Bids
            .Where(b => b.BoliId == boliId)
            .OrderByDescending(b => b.Amount)
            .ToListAsync(cancellationToken);

    public void AddBid(Bid bid) => context.Bids.Add(bid);

    public Task<BoliResult?> GetResultAsync(
        Guid boliId, CancellationToken cancellationToken = default) =>
        context.Results.FirstOrDefaultAsync(r => r.BoliId == boliId, cancellationToken);

    public async Task<IReadOnlyList<BoliResult>> ListPublishedResultsAsync(
        CancellationToken cancellationToken = default) =>
        await context.Results
            .Where(r => r.PublishedAt != null)
            .OrderByDescending(r => r.PublishedAt)
            .ToListAsync(cancellationToken);

    public void AddResult(BoliResult result) => context.Results.Add(result);
}
