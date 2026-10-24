using Microsoft.EntityFrameworkCore;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Domain.Media;
using Sangam.Events.Infrastructure.Persistence;

namespace Sangam.Events.Infrastructure.Repositories;

public sealed class EventPhotoStore(EventsDbContext dbContext) : IEventPhotoStore
{
    public Task<EventPhoto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.EventPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(EventPhoto photo) => dbContext.EventPhotos.Add(photo);

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var photo = await dbContext.EventPhotos.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        // Silent on a miss: a photo replaced twice, or removed by two
        // administrators at once, must not fail.
        if (photo is not null)
        {
            dbContext.EventPhotos.Remove(photo);
        }
    }
}
