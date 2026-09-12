using Microsoft.EntityFrameworkCore;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Domain.Orders;
using Sangam.Businesses.Infrastructure.Persistence;

namespace Sangam.Businesses.Infrastructure.Repositories;

public sealed class OrderRepository(BusinessesDbContext dbContext) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<IReadOnlyList<Order>> ListPlacedAsync(
        Guid buyerMemberId, CancellationToken cancellationToken = default) =>
        ListAsync(o => o.BuyerMemberId == buyerMemberId, cancellationToken);

    public Task<IReadOnlyList<Order>> ListReceivedAsync(
        Guid businessOwnerMemberId, CancellationToken cancellationToken = default) =>
        ListAsync(o => o.BusinessOwnerMemberId == businessOwnerMemberId, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListPlacedForConsumerAsync(
        Guid buyerMemberId, CancellationToken cancellationToken = default) =>
        await dbContext.Orders
            .IgnoreQueryFilters()
            .Where(o => o.BuyerMemberId == buyerMemberId)
            .ToListAsync(cancellationToken);

    public void Add(Order order) => dbContext.Orders.Add(order);

    private async Task<IReadOnlyList<Order>> ListAsync(
        System.Linq.Expressions.Expression<Func<Order, bool>> predicate, CancellationToken cancellationToken) =>
        await dbContext.Orders
            .AsNoTracking()
            .Where(predicate)
            .OrderByDescending(o => o.PlacedAt)
            .ToListAsync(cancellationToken);
}
