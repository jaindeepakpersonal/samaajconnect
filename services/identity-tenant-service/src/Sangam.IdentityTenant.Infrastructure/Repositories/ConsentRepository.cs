using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Consents;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Repositories;

public sealed class ConsentRepository(IdentityTenantDbContext dbContext) : IConsentRepository
{
    public async Task<IReadOnlyList<ConsentRecord>> ListForUserAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        // Tenant-filtered: everything that reads consent is reachable over HTTP.
        await dbContext.ConsentRecords
            .AsNoTracking()
            .Where(record => record.UserId == userId)
            .OrderBy(record => record.RecordedAt)
            .ToListAsync(cancellationToken);

    public void Add(ConsentRecord record) => dbContext.ConsentRecords.Add(record);
}
