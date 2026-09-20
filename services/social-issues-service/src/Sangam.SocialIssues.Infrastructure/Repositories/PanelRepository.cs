using Microsoft.EntityFrameworkCore;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Domain.Panel;
using Sangam.SocialIssues.Infrastructure.Persistence;

namespace Sangam.SocialIssues.Infrastructure.Repositories;

public sealed class PanelRepository(SocialIssuesDbContext dbContext) : IPanelRepository
{
    public Task<bool> IsSeatedAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        dbContext.PanelMembers
            .AsNoTracking()
            .AnyAsync(m => m.MemberId == memberId, cancellationToken);

    public Task<bool> AnySeatedAsync(CancellationToken cancellationToken = default) =>
        dbContext.PanelMembers.AsNoTracking().AnyAsync(cancellationToken);

    public async Task<bool> ReplaceAsync(
        Guid tenantId, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        // The consumer runs on no request and so has no resolved tenant; a
        // filtered read here compares every row against Guid.Empty and finds
        // nothing, so the roster would silently never change. The tenant comes
        // from the event instead. See IPanelRepository.
        var current = await dbContext.PanelMembers
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var wanted = memberIds.ToHashSet();
        var changed = false;

        foreach (var row in current.Where(row => !wanted.Contains(row.MemberId)))
        {
            dbContext.PanelMembers.Remove(row);
            changed = true;
        }

        var have = current.Select(row => row.MemberId).ToHashSet();

        foreach (var memberId in wanted.Where(id => !have.Contains(id)))
        {
            dbContext.PanelMembers.Add(new PanelMember(tenantId, memberId));
            changed = true;
        }

        return changed;
    }
}
