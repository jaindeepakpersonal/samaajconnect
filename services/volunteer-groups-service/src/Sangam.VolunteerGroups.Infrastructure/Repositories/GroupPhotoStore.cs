using Microsoft.EntityFrameworkCore;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Domain.Media;
using Sangam.VolunteerGroups.Infrastructure.Persistence;

namespace Sangam.VolunteerGroups.Infrastructure.Repositories;

public sealed class GroupPhotoStore(VolunteerGroupsDbContext dbContext) : IGroupPhotoStore
{
    public Task<GroupPhoto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.GroupPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(GroupPhoto photo) => dbContext.GroupPhotos.Add(photo);

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var photo = await dbContext.GroupPhotos.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        // Silent on a miss: a photo replaced twice, or removed by two
        // administrators at once, must not fail.
        if (photo is not null)
        {
            dbContext.GroupPhotos.Remove(photo);
        }
    }
}
