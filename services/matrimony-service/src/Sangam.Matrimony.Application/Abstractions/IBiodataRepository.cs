using Sangam.Matrimony.Domain.Biodata;

namespace Sangam.Matrimony.Application.Abstractions;

public interface IBiodataRepository
{
    /// <summary>Tenant-filtered. Active or not - a withdrawn member can still see/reactivate their own.</summary>
    Task<BiodataProfile?> GetByIdAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bypasses the tenant query filter - for the account-erasure consumer,
    /// which has no request and so no resolved tenant, the same shape
    /// member-family-service's own erasure consumer uses.
    /// </summary>
    Task<BiodataProfile?> GetForConsumerAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Active profiles in this Samaaj matching the given filters, excluding the caller, capped at 50.</summary>
    Task<IReadOnlyList<BiodataProfile>> SearchAsync(BiodataSearchFilters filters, CancellationToken cancellationToken = default);

    void Add(BiodataProfile profile);
}

public sealed record BiodataSearchFilters(
    Guid ExcludeMemberId,
    Gender? Gender,
    int? MinAge,
    int? MaxAge,
    Guid? MaritalStatusId,
    int? MinHeightCm,
    int? MaxHeightCm,
    Diet? Diet,
    Guid? LocalityId,
    Guid? ExcludeGautraId);
