using Sangam.Matrimony.Domain.Biodata;

namespace Sangam.Matrimony.Application.Abstractions;

/// <summary>This service's copy of who may keep a biodata. See <see cref="MatrimonyEligibility"/>.</summary>
public interface IEligibilityRepository
{
    /// <summary>The caller's Samaaj only, through the tenant query filter.</summary>
    Task<MatrimonyEligibility?> GetAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bypasses the tenant query filter - for the consumer, which has no request
    /// and so no resolved tenant, the shape `IBiodataRepository.GetForConsumerAsync` uses.
    /// </summary>
    Task<MatrimonyEligibility?> GetForConsumerAsync(Guid tenantId, Guid memberId, CancellationToken cancellationToken = default);

    void Add(MatrimonyEligibility eligibility);

    void Remove(MatrimonyEligibility eligibility);
}
