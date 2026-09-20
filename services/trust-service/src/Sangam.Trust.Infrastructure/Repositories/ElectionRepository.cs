using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Domain.Elections;
using Sangam.Trust.Infrastructure.Persistence;

namespace Sangam.Trust.Infrastructure.Repositories;

public sealed class ElectionRepository(TrustDbContext dbContext) : IElectionRepository
{
    public Task<TrustElection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Elections
            // Candidates, never votes. See IElectionRepository.
            .Include(e => e.Candidates)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TrustElection>> ListForTrustAsync(
        Guid trustId, CancellationToken cancellationToken = default) =>
        await dbContext.Elections
            .AsNoTracking()
            .Include(e => e.Candidates)
            .Where(e => e.TrustId == trustId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(TrustElection election) => dbContext.Elections.Add(election);
}

public sealed class VoteRepository(
    TrustDbContext dbContext,
    IServiceScopeFactory scopeFactory)
    : IVoteRepository
{
    /// <summary>Postgres unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    public Task<Vote?> FindForVoterAsync(
        Guid electionId, Guid voterMemberId, CancellationToken cancellationToken = default) =>
        dbContext.Votes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                v => v.ElectionId == electionId && v.VoterMemberId == voterMemberId,
                cancellationToken);

    /// <summary>
    /// Inserts one vote on its own scope, context and connection - the
    /// identical reasoning volunteer-groups-service's own
    /// <c>VoteRepository.TryCastAsync</c> documents: contention (this insert
    /// must not sit inside the request's own transaction, held open from the
    /// election read to the response) and recoverability (a unique violation
    /// poisons the change tracker it happened on; a throwaway context is
    /// simply discarded rather than left retrying a failed insert on the
    /// next SaveChanges).
    /// </summary>
    public async Task<bool> TryCastAsync(Vote vote, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TrustDbContext>();

        context.Votes.Add(vote);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            return false;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> TallyAsync(
        Guid electionId, CancellationToken cancellationToken = default) =>
        await dbContext.Votes
            .AsNoTracking()
            .Where(v => v.ElectionId == electionId)
            .GroupBy(v => v.CandidateId)
            .Select(g => new { CandidateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CandidateId, x => x.Count, cancellationToken);
}
