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

    public async Task<IReadOnlyList<ChildProfile>> ListByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default) =>
        // Tenant-filtered, deliberately not IgnoreQueryFilters: the ids come
        // from another service and a caller must not be able to name a child in
        // a Samaaj they do not administer by handing over the right GUID.
        await dbContext.ChildProfiles
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ChildProfile>> ListForConsumerAsync(
        Guid familyId, CancellationToken cancellationToken = default) =>
        await dbContext.ChildProfiles
            .IgnoreQueryFilters()
            .Where(c => c.FamilyId == familyId)
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// The consent is an owned type on the child, so this filters on
    /// <c>ParentalConsent.GivenByMemberId</c> — the column EF maps it to on the
    /// same row, not a join.
    /// </remarks>
    public async Task<IReadOnlyList<ChildProfile>> ListByConsentGiverAsync(
        Guid memberProfileId, CancellationToken cancellationToken = default) =>
        await dbContext.ChildProfiles
            .IgnoreQueryFilters()
            .Where(c => c.ParentalConsent != null
                && c.ParentalConsent.GivenByMemberId == memberProfileId)
            .ToListAsync(cancellationToken);

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
