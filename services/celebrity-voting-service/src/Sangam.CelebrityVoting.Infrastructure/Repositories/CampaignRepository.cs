using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Domain.Campaigns;
using Sangam.CelebrityVoting.Infrastructure.Persistence;

namespace Sangam.CelebrityVoting.Infrastructure.Repositories;

public sealed class CampaignRepository(CelebrityVotingDbContext dbContext) : ICampaignRepository
{
    public Task<VotingCampaign?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Campaigns
            // Candidates, never votes. See ICampaignRepository.
            .Include(c => c.Candidates)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<VotingCampaign>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Campaigns
            .AsNoTracking()
            .Include(c => c.Candidates)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(VotingCampaign campaign) => dbContext.Campaigns.Add(campaign);
}

public sealed class VoteRepository(
    CelebrityVotingDbContext dbContext,
    IServiceScopeFactory scopeFactory)
    : IVoteRepository
{
    /// <summary>Postgres unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    public Task<Vote?> FindForVoterAsync(
        Guid campaignId, Guid voterMemberId, CancellationToken cancellationToken = default) =>
        dbContext.Votes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                v => v.CampaignId == campaignId && v.VoterMemberId == voterMemberId,
                cancellationToken);

    /// <summary>
    /// Inserts one vote on its own scope, context and connection.
    /// </summary>
    /// <remarks>
    /// Two reasons, and both matter on this path.
    ///
    /// <b>Contention.</b> Casting a vote is the busiest write on the platform.
    /// Doing it on the request's context would put the insert inside the
    /// transaction <c>TransactionBehavior</c> opens around the whole command,
    /// held from the campaign read to the response - so voters would wait on
    /// each other for the duration of a request rather than of an insert.
    ///
    /// <b>Recoverability.</b> A unique violation poisons the EF change tracker
    /// it happened on: the failed entry stays Added, and the next
    /// <c>SaveChanges</c> retries it. On a throwaway context that does not
    /// matter, because the context is thrown away. On the request's context it
    /// would turn one refused vote into a failure of everything after it.
    ///
    /// Returning false rather than throwing, because a member pressing the
    /// button twice is the ordinary case here, not an exceptional one.
    /// </remarks>
    public async Task<bool> TryCastAsync(Vote vote, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CelebrityVotingDbContext>();

        context.Votes.Add(vote);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // The index refused it: this member already has a vote in this
            // campaign. That is the guarantee working.
            return false;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> TallyAsync(
        Guid campaignId, CancellationToken cancellationToken = default) =>
        // GROUP BY in the database. Counting a loaded collection would mean
        // loading every vote in the campaign to produce a handful of numbers.
        await dbContext.Votes
            .AsNoTracking()
            .Where(v => v.CampaignId == campaignId)
            .GroupBy(v => v.CandidateId)
            .Select(g => new { CandidateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CandidateId, x => x.Count, cancellationToken);

    public Task<CampaignResult?> FindResultAsync(
        Guid campaignId, CancellationToken cancellationToken = default) =>
        dbContext.Results
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CampaignId == campaignId, cancellationToken);

    public void AddResult(CampaignResult result) => dbContext.Results.Add(result);
}
