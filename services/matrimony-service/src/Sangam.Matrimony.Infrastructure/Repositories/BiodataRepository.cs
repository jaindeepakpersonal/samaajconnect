using Microsoft.EntityFrameworkCore;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Domain.Biodata;
using Sangam.Matrimony.Infrastructure.Persistence;

namespace Sangam.Matrimony.Infrastructure.Repositories;

public sealed class BiodataRepository(MatrimonyDbContext dbContext) : IBiodataRepository
{
    public Task<BiodataProfile?> GetByIdAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        dbContext.BiodataProfiles.FirstOrDefaultAsync(p => p.Id == memberId, cancellationToken);

    public Task<BiodataProfile?> GetForConsumerAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        dbContext.BiodataProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == memberId, cancellationToken);

    public async Task<IReadOnlyList<BiodataProfile>> SearchAsync(
        BiodataSearchFilters filters, CancellationToken cancellationToken = default)
    {
        var query = dbContext.BiodataProfiles
            .AsNoTracking()
            .Where(p => p.IsActive && p.Id != filters.ExcludeMemberId)
            // Only members who may keep a biodata: unmarried or divorced, as
            // their own profile says (MatrimonyEligibility).
            .Where(p => dbContext.Eligibility.Any(e => e.MemberId == p.Id && e.IsEligible));

        if (filters.Gender is { } gender)
        {
            query = query.Where(p => p.Gender == gender);
        }

        if (filters.MaritalStatusId is { } maritalStatusId)
        {
            query = query.Where(p => p.MaritalStatusId == maritalStatusId);
        }

        if (filters.MinHeightCm is { } minHeight)
        {
            query = query.Where(p => p.HeightCm != null && p.HeightCm >= minHeight);
        }

        if (filters.MaxHeightCm is { } maxHeight)
        {
            query = query.Where(p => p.HeightCm != null && p.HeightCm <= maxHeight);
        }

        if (filters.Diet is { } diet)
        {
            query = query.Where(p => p.Diet == diet);
        }

        if (filters.LocalityId is { } localityId)
        {
            query = query.Where(p => p.LocalityId == localityId);
        }

        if (filters.ExcludeGautraId is { } excludeGautraId)
        {
            query = query.Where(p => p.GautraId == null || p.GautraId != excludeGautraId);
        }

        // Age is derived from DateOfBirth, not stored, so the min/max age
        // filters translate to a date-of-birth range computed from today -
        // the same "eligibility computed, not stored" shape used throughout
        // this call.
        if (filters.MinAge is { } minAge)
        {
            var latestBirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-minAge);
            query = query.Where(p => p.DateOfBirth <= latestBirthDate);
        }

        if (filters.MaxAge is { } maxAge)
        {
            var earliestBirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-maxAge - 1);
            query = query.Where(p => p.DateOfBirth > earliestBirthDate);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public void Add(BiodataProfile profile) => dbContext.BiodataProfiles.Add(profile);
}
