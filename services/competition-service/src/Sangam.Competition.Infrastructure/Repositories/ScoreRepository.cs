using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Domain.Entries;
using Sangam.Competition.Infrastructure.Persistence;

namespace Sangam.Competition.Infrastructure.Repositories;

public sealed class ScoreRepository(
    CompetitionDbContext dbContext,
    IServiceScopeFactory scopeFactory)
    : IScoreRepository
{
    /// <summary>Postgres unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    public Task<Score?> FindAsync(
        Guid entryId, Guid judgeMemberId, CancellationToken cancellationToken = default) =>
        dbContext.Scores
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.EntryId == entryId && s.JudgeMemberId == judgeMemberId, cancellationToken);

    /// <summary>
    /// Upserts on its own scope, context and connection - the same shape
    /// celebrity-voting-service's own `VoteRepository.TryCastAsync` uses, and
    /// for the same two reasons: it keeps a judge scoring many entries from
    /// serialising against other judges for the length of a request rather
    /// than of one write, and it keeps a unique-violation (two requests from
    /// the same judge for the same entry racing each other) from poisoning
    /// the request's own change tracker.
    ///
    /// Unlike a vote, losing the race here is not refused - it is retried as
    /// an update, because correcting a mark is the point of this being an
    /// upsert in the first place.
    /// </summary>
    public async Task<Score> UpsertAsync(
        Guid tenantId,
        Guid entryId,
        Guid judgeMemberId,
        int value,
        string? comments,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CompetitionDbContext>();

        var existing = await context.Scores.FirstOrDefaultAsync(
            s => s.EntryId == entryId && s.JudgeMemberId == judgeMemberId, cancellationToken);

        if (existing is not null)
        {
            existing.Update(value, comments, now);
            await context.SaveChangesAsync(cancellationToken);

            return existing;
        }

        var score = new Score(tenantId, entryId, judgeMemberId, value, comments, now);
        context.Scores.Add(score);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return score;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Another request from this judge for this entry won the race.
            // Re-read on a fresh scope (the failed insert poisoned this
            // context's tracker) and update instead.
            await using var retryScope = scopeFactory.CreateAsyncScope();
            var retryContext = retryScope.ServiceProvider.GetRequiredService<CompetitionDbContext>();

            var theirs = await retryContext.Scores.FirstAsync(
                s => s.EntryId == entryId && s.JudgeMemberId == judgeMemberId, cancellationToken);

            theirs.Update(value, comments, now);
            await retryContext.SaveChangesAsync(cancellationToken);

            return theirs;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, double>> AverageByEntryAsync(
        IReadOnlyList<Guid> entryIds, CancellationToken cancellationToken = default)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<Guid, double>();
        }

        return await dbContext.Scores
            .AsNoTracking()
            .Where(s => entryIds.Contains(s.EntryId))
            .GroupBy(s => s.EntryId)
            .Select(g => new { EntryId = g.Key, Average = g.Average(s => (double)s.Value) })
            .ToDictionaryAsync(x => x.EntryId, x => x.Average, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> ListMineAsync(
        IReadOnlyList<Guid> entryIds, Guid judgeMemberId, CancellationToken cancellationToken = default)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await dbContext.Scores
            .AsNoTracking()
            .Where(s => entryIds.Contains(s.EntryId) && s.JudgeMemberId == judgeMemberId)
            .ToDictionaryAsync(s => s.EntryId, s => s.Value, cancellationToken);
    }
}
