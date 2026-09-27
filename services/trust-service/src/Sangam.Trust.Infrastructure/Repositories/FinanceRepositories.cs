using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Domain.Donations;
using Sangam.Trust.Domain.Expenses;
using Sangam.Trust.Infrastructure.Persistence;

namespace Sangam.Trust.Infrastructure.Repositories;

// Collection is referenced as `Domain.Collections.Collection`, never bare -
// see that type's own remarks.

public sealed class DonationRepository(TrustDbContext dbContext) : IDonationRepository
{
    /// <summary>
    /// Namespaces this service's advisory locks, so a receipt lock can never
    /// collide with a lock some other code takes on the same 32-bit key.
    /// </summary>
    private const int ReceiptLockNamespace = 0x7472_7374;   // "trst"

    public Task<Donation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Donations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Donation>> ListByTrustAsync(
        Guid trustId, DonationStatus? status, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Donations.AsNoTracking().Where(d => d.TrustId == trustId);

        if (status is { } wanted)
        {
            query = query.Where(d => d.Status == wanted);
        }

        return await query.OrderByDescending(d => d.CreatedAt).ToListAsync(cancellationToken);
    }

    public void Add(Donation donation) => dbContext.Donations.Add(donation);

    public async Task<int> ReserveNextReceiptNumberAsync(Guid trustId, CancellationToken cancellationToken = default)
    {
        // The two-key form, (namespace, key): the Trust id folded to 32 bits,
        // spelled out rather than GetHashCode so every replica of this service
        // takes the same lock for the same Trust. Two Trusts sharing a fold
        // only ever wait for each other briefly - the lock serialises, it does
        // not decide the number.
        var bytes = trustId.ToByteArray();
        var key = BitConverter.ToInt32(bytes, 0)
            ^ BitConverter.ToInt32(bytes, 4)
            ^ BitConverter.ToInt32(bytes, 8)
            ^ BitConverter.ToInt32(bytes, 12);

        await dbContext.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({ReceiptLockNamespace}, {key})", cancellationToken);

        // Unfiltered by tenant on purpose: the sequence is every receipt this
        // Trust has issued, and must not depend on how the caller's tenant was
        // resolved. A Trust belongs to exactly one Samaaj, so nothing outside
        // it can be counted.
        var highest = await dbContext.Donations
            .IgnoreQueryFilters()
            .Where(d => d.TrustId == trustId && d.ReceiptNumber != null)
            .MaxAsync(d => d.ReceiptNumber, cancellationToken);

        return (highest ?? 0) + 1;
    }
}

public sealed class ExpenseRepository(TrustDbContext dbContext) : IExpenseRepository
{
    public Task<Expense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Expenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Expense>> ListByTrustAsync(
        Guid trustId, ExpenseStatus? status, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Expenses.AsNoTracking().Where(e => e.TrustId == trustId);

        if (status is { } wanted)
        {
            query = query.Where(e => e.Status == wanted);
        }

        return await query.OrderByDescending(e => e.RecordedAt).ToListAsync(cancellationToken);
    }

    public void Add(Expense expense) => dbContext.Expenses.Add(expense);
}

public sealed class CollectionRepository(
    TrustDbContext dbContext,
    IServiceScopeFactory scopeFactory)
    : ICollectionRepository
{
    /// <summary>Postgres unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    public Task<Domain.Collections.Collection?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Collections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Domain.Collections.Collection>> ListByTrustAsync(
        Guid trustId, Domain.Collections.CollectionStatus? status, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Collections.AsNoTracking().Where(c => c.TrustId == trustId);

        if (status is { } wanted)
        {
            query = query.Where(c => c.Status == wanted);
        }

        return await query.OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
    }

    public Task<Domain.Collections.Collection?> FindBySourceAsync(
        Domain.Collections.CollectionSourceType sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default) =>
        dbContext.Collections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SourceType == sourceType && c.SourceId == sourceId, cancellationToken);

    public async Task<IReadOnlyList<Domain.Collections.Collection>> ListDueForPayerAsync(
        Guid payerMemberId, CancellationToken cancellationToken = default) =>
        await dbContext.Collections
            .AsNoTracking()
            .Where(c => c.PayerMemberId == payerMemberId && c.Status == Domain.Collections.CollectionStatus.Due)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Domain.Collections.Collection?> GetBySourceAsync(
        Domain.Collections.CollectionSourceType sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default) =>
        dbContext.Collections
            .FirstOrDefaultAsync(c => c.SourceType == sourceType && c.SourceId == sourceId, cancellationToken);

    public void Add(Domain.Collections.Collection collection) => dbContext.Collections.Add(collection);

    public async Task<bool> TryOpenAsync(
        Domain.Collections.Collection collection, CancellationToken cancellationToken = default)
    {
        // Its own scope, so a refused insert cannot leave a failed entity
        // tracked on the caller's context and poison its next SaveChanges.
        // The domain event goes to the outbox in this same save, so a
        // Collection and its "due" event land together or not at all.
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TrustDbContext>();

        context.Collections.Add(collection);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // A redelivery: this source already has its Collection. That is
            // the guarantee working.
            return false;
        }
    }
}
