using Microsoft.EntityFrameworkCore;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Domain.Media;
using Sangam.Competition.Infrastructure.Persistence;

namespace Sangam.Competition.Infrastructure.Repositories;

public sealed class CompetitionPhotoStore(CompetitionDbContext dbContext) : ICompetitionPhotoStore
{
    public Task<CompetitionPhoto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.CompetitionPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(CompetitionPhoto photo) => dbContext.CompetitionPhotos.Add(photo);

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var photo = await dbContext.CompetitionPhotos.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        // Silent on a miss: a photo replaced twice, or removed by two
        // organisers at once, must not fail.
        if (photo is not null)
        {
            dbContext.CompetitionPhotos.Remove(photo);
        }
    }
}
