using Microsoft.EntityFrameworkCore;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Domain.Biodata;
using Sangam.Matrimony.Infrastructure.Persistence;

namespace Sangam.Matrimony.Infrastructure.Repositories;

public sealed class EligibilityRepository(MatrimonyDbContext dbContext) : IEligibilityRepository
{
    public Task<MatrimonyEligibility?> GetAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        dbContext.Eligibility.FirstOrDefaultAsync(e => e.MemberId == memberId, cancellationToken);

    public Task<MatrimonyEligibility?> GetForConsumerAsync(
        Guid tenantId, Guid memberId, CancellationToken cancellationToken = default) =>
        dbContext.Eligibility.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.MemberId == memberId, cancellationToken);

    public void Add(MatrimonyEligibility eligibility) => dbContext.Eligibility.Add(eligibility);

    public void Remove(MatrimonyEligibility eligibility) => dbContext.Eligibility.Remove(eligibility);
}
