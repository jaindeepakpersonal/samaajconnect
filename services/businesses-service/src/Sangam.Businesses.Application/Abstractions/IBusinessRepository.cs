using Sangam.Businesses.Domain.Businesses;

namespace Sangam.Businesses.Application.Abstractions;

public interface IBusinessRepository
{
    Task<Business?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every business this member owns, active or not - for the management screen.</summary>
    Task<IReadOnlyList<Business>> ListOwnedAsync(Guid ownerMemberId, CancellationToken cancellationToken = default);

    /// <summary>Active businesses in this Samaaj matching the given filters, capped at the caller's requested limit.</summary>
    Task<IReadOnlyList<Business>> SearchAsync(BusinessSearchFilters filters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bypasses the tenant query filter - for the account-erasure consumer,
    /// which has no request and so no resolved tenant, the same shape
    /// Matrimony's own `IBiodataRepository.GetForConsumerAsync` uses.
    /// </summary>
    Task<IReadOnlyList<Business>> ListForConsumerAsync(Guid ownerMemberId, CancellationToken cancellationToken = default);

    void Add(Business business);

    /// <summary>
    /// Hard delete, used only by the account-erasure consumer. A business is
    /// wholly the owner's own commercial listing - unlike Matrimony's
    /// `Proposal`, no other party has a stake in the row itself, only in the
    /// `Order`s referencing it by plain id.
    /// </summary>
    void Remove(Business business);
}

public sealed record BusinessSearchFilters(
    Guid? CategoryId,
    Guid? CityId,
    Guid? LocalityId,
    string? Term,
    int Limit);
