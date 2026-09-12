using Microsoft.EntityFrameworkCore;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Domain.Proposals;
using Sangam.Matrimony.Infrastructure.Persistence;

namespace Sangam.Matrimony.Infrastructure.Repositories;

public sealed class ProposalRepository(MatrimonyDbContext dbContext) : IProposalRepository
{
    public Task<Proposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Proposals.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> HasActiveBetweenAsync(
        Guid memberIdA, Guid memberIdB, CancellationToken cancellationToken = default) =>
        dbContext.Proposals.AnyAsync(p =>
            (p.Status == ProposalStatus.Pending || p.Status == ProposalStatus.Accepted)
            && ((p.FromMemberId == memberIdA && p.ToMemberId == memberIdB)
                || (p.FromMemberId == memberIdB && p.ToMemberId == memberIdA)),
            cancellationToken);

    public Task<IReadOnlyList<Proposal>> ListSentAsync(Guid fromMemberId, CancellationToken cancellationToken = default) =>
        ListAsync(p => p.FromMemberId == fromMemberId, cancellationToken);

    public Task<IReadOnlyList<Proposal>> ListReceivedAsync(Guid toMemberId, CancellationToken cancellationToken = default) =>
        ListAsync(p => p.ToMemberId == toMemberId, cancellationToken);

    public async Task<IReadOnlyList<Proposal>> ListForConsumerAsync(
        Guid memberId, CancellationToken cancellationToken = default) =>
        await dbContext.Proposals
            .IgnoreQueryFilters()
            .Where(p => p.FromMemberId == memberId || p.ToMemberId == memberId)
            .ToListAsync(cancellationToken);

    public void Add(Proposal proposal) => dbContext.Proposals.Add(proposal);

    private async Task<IReadOnlyList<Proposal>> ListAsync(
        System.Linq.Expressions.Expression<Func<Proposal, bool>> predicate, CancellationToken cancellationToken) =>
        await dbContext.Proposals
            .AsNoTracking()
            .Where(predicate)
            .OrderByDescending(p => p.SentAt)
            .ToListAsync(cancellationToken);
}
