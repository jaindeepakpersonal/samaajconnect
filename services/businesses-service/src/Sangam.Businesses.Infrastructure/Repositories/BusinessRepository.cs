using Microsoft.EntityFrameworkCore;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Domain.Businesses;
using Sangam.Businesses.Infrastructure.Persistence;

namespace Sangam.Businesses.Infrastructure.Repositories;

public sealed class BusinessRepository(BusinessesDbContext dbContext) : IBusinessRepository
{
    public Task<Business?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Businesses.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Business>> ListOwnedAsync(
        Guid ownerMemberId, CancellationToken cancellationToken = default) =>
        await dbContext.Businesses
            .Where(b => b.OwnerMemberId == ownerMemberId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Business>> SearchAsync(
        BusinessSearchFilters filters, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Businesses.AsNoTracking().Where(b => b.IsActive);

        if (filters.CategoryId is { } categoryId)
        {
            query = query.Where(b => b.CategoryId == categoryId);
        }

        if (filters.CityId is { } cityId)
        {
            query = query.Where(b => b.CityId == cityId);
        }

        if (filters.LocalityId is { } localityId)
        {
            query = query.Where(b => b.LocalityId == localityId);
        }

        if (!string.IsNullOrWhiteSpace(filters.Term))
        {
            var term = filters.Term.Trim();
            query = query.Where(b => EF.Functions.ILike(b.Name, $"%{term}%"));
        }

        return await query
            .OrderByDescending(b => b.CreatedAt)
            .Take(filters.Limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Business>> ListForConsumerAsync(
        Guid ownerMemberId, CancellationToken cancellationToken = default) =>
        await dbContext.Businesses
            .IgnoreQueryFilters()
            .Where(b => b.OwnerMemberId == ownerMemberId)
            .ToListAsync(cancellationToken);

    public void Add(Business business) => dbContext.Businesses.Add(business);

    public void Remove(Business business) => dbContext.Businesses.Remove(business);
}
