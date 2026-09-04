using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Media;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Repositories;

/// <summary>
/// Keeps Samaaj logos in this service's own database. See
/// <see cref="ILogoStore"/> for why here, and why behind an interface.
/// </summary>
public sealed class LogoStore(IdentityTenantDbContext dbContext) : ILogoStore
{
    public Task<TenantLogo?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.TenantLogos
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public void Add(TenantLogo logo) => dbContext.TenantLogos.Add(logo);

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var logo = await dbContext.TenantLogos
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        // Silent on a miss. A logo replaced twice, or two administrators
        // removing one at the same moment, must not fail - and there is nothing
        // for a caller to do differently either way.
        if (logo is not null)
        {
            dbContext.TenantLogos.Remove(logo);
        }
    }

    public async Task RemoveAllForTenantAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        var logos = await dbContext.TenantLogos
            .Where(l => l.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        if (logos.Count > 0)
        {
            dbContext.TenantLogos.RemoveRange(logos);
        }
    }
}
