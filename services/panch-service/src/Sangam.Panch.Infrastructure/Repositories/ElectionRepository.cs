using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Domain.Elections;
using Sangam.Panch.Domain.Panel;
using Sangam.Panch.Infrastructure.Persistence;

namespace Sangam.Panch.Infrastructure.Repositories;

public sealed class ElectionRepository(PanchDbContext dbContext) : IElectionRepository
{
    public Task<PanchElection?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Elections
            // Candidates, never ballots. See IElectionRepository.
            .Include(e => e.Candidates)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PanchElection>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Elections
            .AsNoTracking()
            .Include(e => e.Candidates)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(PanchElection election) => dbContext.Elections.Add(election);
}

public sealed class BallotRepository(
    PanchDbContext dbContext,
    IServiceScopeFactory scopeFactory)
    : IBallotRepository
{
    /// <summary>Postgres unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    public async Task<IReadOnlyList<Guid>?> FindChoicesAsync(
        Guid electionId, Guid voterMemberId, CancellationToken cancellationToken = default)
    {
        var voted = await dbContext.Ballots
            .AsNoTracking()
            .AnyAsync(b => b.ElectionId == electionId && b.VoterMemberId == voterMemberId, cancellationToken);

        if (!voted)
        {
            return null;
        }

        return await dbContext.Votes
            .AsNoTracking()
            .Where(v => v.ElectionId == electionId && v.VoterMemberId == voterMemberId)
            .Select(v => v.CandidateId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryCastAsync(
        Ballot ballot, IReadOnlyList<Vote> votes, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PanchDbContext>();

        context.Ballots.Add(ballot);
        context.Votes.AddRange(votes);

        try
        {
            // One SaveChanges is one transaction: the ballot row and every vote
            // land together or not at all, so a refused second ballot leaves
            // nothing behind.
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // The index refused it: this member already has a ballot in this
            // election. That is the guarantee working.
            return false;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> TallyAsync(
        Guid electionId, CancellationToken cancellationToken = default) =>
        // GROUP BY in the database, never a count over loaded rows.
        await dbContext.Votes
            .AsNoTracking()
            .Where(v => v.ElectionId == electionId)
            .GroupBy(v => v.CandidateId)
            .Select(g => new { CandidateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CandidateId, x => x.Count, cancellationToken);

    public Task<ElectionResult?> FindResultAsync(
        Guid electionId, CancellationToken cancellationToken = default) =>
        dbContext.Results
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ElectionId == electionId, cancellationToken);

    public void AddResult(ElectionResult result) => dbContext.Results.Add(result);
}

public sealed class PanelRepository(PanchDbContext dbContext) : IPanelRepository
{
    public async Task<IReadOnlyList<PanelMember>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.PanelMembers
            .OrderBy(m => m.SeatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

    public void Add(PanelMember member) => dbContext.PanelMembers.Add(member);

    public void Remove(PanelMember member) => dbContext.PanelMembers.Remove(member);
}
