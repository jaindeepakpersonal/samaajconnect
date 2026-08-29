using Microsoft.EntityFrameworkCore;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Domain.Groups;
using Sangam.VolunteerGroups.Infrastructure.Persistence;

namespace Sangam.VolunteerGroups.Infrastructure.Repositories;

public sealed class GroupRepository(VolunteerGroupsDbContext dbContext) : IGroupRepository
{
    public Task<VolunteerGroup?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Groups
            .Include(g => g.Applications)
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public async Task<IReadOnlyList<VolunteerGroup>> ListAsync(
        GroupStatus? status, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Groups
            .AsNoTracking()

            // Both are needed by the response: the member count, and whether
            // the asking member is in the group or has applied.
            .Include(g => g.Members)
            .Include(g => g.Applications)
            .AsQueryable();

        if (status is { } wanted)
        {
            query = query.Where(g => g.Status == wanted);
        }

        return await query.OrderBy(g => g.Name).ToListAsync(cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Groups.AnyAsync(g => g.Name == name, cancellationToken);

    public void Add(VolunteerGroup group) => dbContext.Groups.Add(group);
}
