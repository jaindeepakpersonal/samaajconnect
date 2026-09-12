using Sangam.Businesses.Domain.Orders;

namespace Sangam.Businesses.Application.Abstractions;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Placed by this member, most recent first.</summary>
    Task<IReadOnlyList<Order>> ListPlacedAsync(Guid buyerMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Against any business this member owns, most recent first - a plain
    /// filter on the snapshotted <see cref="Order.BusinessOwnerMemberId"/>,
    /// needing no join back to <see cref="Domain.Businesses.Business"/>.
    /// </summary>
    Task<IReadOnlyList<Order>> ListReceivedAsync(Guid businessOwnerMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every order this member placed, in any Samaaj query filter's blind
    /// spot - for the account-erasure consumer, the same "no request, no
    /// resolved tenant" shape as <see cref="IBusinessRepository.ListForConsumerAsync"/>.
    /// </summary>
    Task<IReadOnlyList<Order>> ListPlacedForConsumerAsync(Guid buyerMemberId, CancellationToken cancellationToken = default);

    void Add(Order order);
}
