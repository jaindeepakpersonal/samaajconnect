using Microsoft.EntityFrameworkCore;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Domain.Members;
using Sangam.Trust.Infrastructure.Persistence;

namespace Sangam.Trust.Infrastructure.Repositories;

public sealed class TrustMemberRepository(TrustDbContext dbContext) : ITrustMemberRepository
{
    public Task<TrustMember?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.TrustMembers.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<TrustMember?> FindAsync(
        Guid trustId, Guid memberId, CancellationToken cancellationToken = default) =>
        dbContext.TrustMembers
            .FirstOrDefaultAsync(m => m.TrustId == trustId && m.MemberId == memberId, cancellationToken);

    public async Task<IReadOnlyList<TrustMember>> ListByTrustAsync(
        Guid trustId, CancellationToken cancellationToken = default) =>
        await dbContext.TrustMembers
            .AsNoTracking()
            .Where(m => m.TrustId == trustId)
            .ToListAsync(cancellationToken);

    public void Add(TrustMember member) => dbContext.TrustMembers.Add(member);
}
