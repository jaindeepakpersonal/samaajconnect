using Microsoft.EntityFrameworkCore;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Domain.Events;
using Sangam.Events.Infrastructure.Persistence;

namespace Sangam.Events.Infrastructure.Repositories;

public sealed class EventRepository(EventsDbContext dbContext) : IEventRepository
{
    public Task<SamaajEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Events
            .Include(e => e.Registrations)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SamaajEvent>> ListAsync(
        bool publishedOnly,
        bool upcomingOnly,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Registrations are included because the response carries the counts
        // and the asking member's own standing - the two things every row of
        // the wireframe's list shows.
        var query = dbContext.Events
            .AsNoTracking()
            .Include(e => e.Registrations)
            .AsQueryable();

        if (publishedOnly)
        {
            query = query.Where(e => e.Status != EventStatus.Draft);
        }

        if (upcomingOnly)
        {
            // A cancelled event stays in the list until it would have happened:
            // somebody who planned around it should see that it is off, not
            // find it simply gone.
            query = query.Where(e => e.StartAt >= now);
        }

        return await query.OrderBy(e => e.StartAt).ToListAsync(cancellationToken);
    }

    public void Add(SamaajEvent samaajEvent) => dbContext.Events.Add(samaajEvent);
}
