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
            .Include(e => e.Schedule)
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
            .Include(e => e.Schedule)
            .AsSplitQuery()
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
            //
            // "Upcoming" means not over yet, not "not started yet" - an event under
            // way is the one people most need to find, and filtering on StartAt
            // hid it the moment it began (2026-10-24). An event with no end time
            // is taken to run for SamaajEvent.UntimedEventLength after it starts.
            var untimedCutoff = now - SamaajEvent.UntimedEventLength;

            query = query.Where(e => e.EndAt != null ? e.EndAt >= now : e.StartAt >= untimedCutoff);
        }

        return await query.OrderBy(e => e.StartAt).ToListAsync(cancellationToken);
    }

    public void Add(SamaajEvent samaajEvent) => dbContext.Events.Add(samaajEvent);
}
