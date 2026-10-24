using Microsoft.EntityFrameworkCore;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Domain.Families;
using Sangam.MemberFamily.Infrastructure.Persistence;

namespace Sangam.MemberFamily.Infrastructure.Repositories;

public sealed class FamilyRepository(MemberFamilyDbContext dbContext) : IFamilyRepository
{
    public Task<Family?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Families
            .Include(f => f.Members)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public Task<Family?> GetByCodeAsync(string familyCode, CancellationToken cancellationToken = default) =>
        dbContext.Families
            .Include(f => f.Members)
            .FirstOrDefaultAsync(f => f.FamilyCode == familyCode, cancellationToken);

    public Task<Family?> GetForMemberAsync(Guid memberProfileId, CancellationToken cancellationToken = default) =>
        dbContext.Families
            .Include(f => f.Members)
            // Includes a pending request on purpose: someone waiting on one
            // household should not be able to ask a second one at the same
            // time, or two heads could each accept them.
            .FirstOrDefaultAsync(
                f => f.Members.Any(m =>
                    m.MemberProfileId == memberProfileId
                    && m.Status != FamilyMemberStatus.Rejected),
                cancellationToken);

    public Task<bool> CodeExistsAsync(string familyCode, CancellationToken cancellationToken = default) =>
        dbContext.Families.AnyAsync(f => f.FamilyCode == familyCode, cancellationToken);

    public Task<Family?> GetForConsumerAsync(
        Guid memberProfileId, CancellationToken cancellationToken = default) =>
        dbContext.Families
            .IgnoreQueryFilters()
            .Include(f => f.Members)
            .FirstOrDefaultAsync(
                f => f.Members.Any(m =>
                    m.MemberProfileId == memberProfileId
                    && m.Status != FamilyMemberStatus.Rejected),
                cancellationToken);

    public async Task<IReadOnlyList<Family>> ListForMembersAsync(
        IReadOnlyCollection<Guid> memberProfileIds, CancellationToken cancellationToken = default)
    {
        if (memberProfileIds.Count == 0)
        {
            return [];
        }

        // Through Families, which carry the tenant filter - never FamilyMembers
        // directly, which have no tenant of their own.
        return await dbContext.Families
            .AsNoTracking()
            .Include(f => f.Members)
            .Where(f => f.Members.Any(m =>
                memberProfileIds.Contains(m.MemberProfileId) && m.Status == FamilyMemberStatus.Active))
            .ToListAsync(cancellationToken);
    }

    public void Add(Family family) => dbContext.Families.Add(family);
}
