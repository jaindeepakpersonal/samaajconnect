using Microsoft.EntityFrameworkCore;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Infrastructure.Persistence;

namespace Sangam.MemberFamily.Infrastructure.Repositories;

public sealed class ChildRepository(MemberFamilyDbContext dbContext) : IChildRepository
{
    public Task<ChildProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ChildProfiles.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<ChildProfile?> GetForConsumerAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ChildProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ChildProfile>> ListForFamilyAsync(
        Guid familyId, CancellationToken cancellationToken = default) =>
        await dbContext.ChildProfiles
            .AsNoTracking()
            .Where(c => c.FamilyId == familyId)
            .OrderBy(c => c.DateOfBirth)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ChildProfile>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.ChildProfiles.AsNoTracking().ToListAsync(cancellationToken);

    public void Add(ChildProfile child) => dbContext.ChildProfiles.Add(child);
}

public sealed class ChildConversionRepository(MemberFamilyDbContext dbContext)
    : IChildConversionRepository
{
    public Task<ChildConversionRequest?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ChildConversionRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<ChildConversionRequest?> GetPendingForChildAsync(
        Guid childProfileId, CancellationToken cancellationToken = default) =>
        dbContext.ChildConversionRequests.FirstOrDefaultAsync(
            r => r.ChildProfileId == childProfileId && r.Status == ConversionStatus.Pending,
            cancellationToken);

    public async Task<IReadOnlyList<ChildConversionRequest>> ListPendingAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.ChildConversionRequests
            .AsNoTracking()
            .Where(r => r.Status == ConversionStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(cancellationToken);

    public void Add(ChildConversionRequest request) => dbContext.ChildConversionRequests.Add(request);
}
