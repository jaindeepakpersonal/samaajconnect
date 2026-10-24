using Microsoft.EntityFrameworkCore;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Domain.Media;
using Sangam.Businesses.Infrastructure.Persistence;

namespace Sangam.Businesses.Infrastructure.Repositories;

public sealed class BusinessImageStore(BusinessesDbContext dbContext) : IBusinessImageStore
{
    public Task<BusinessImage?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.BusinessImages
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public void Add(BusinessImage image) => dbContext.BusinessImages.Add(image);

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var image = await dbContext.BusinessImages.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        // Silent on a miss: an image replaced twice, or removed from two tabs
        // at once, must not fail.
        if (image is not null)
        {
            dbContext.BusinessImages.Remove(image);
        }
    }
}
