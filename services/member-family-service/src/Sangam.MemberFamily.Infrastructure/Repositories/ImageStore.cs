using Microsoft.EntityFrameworkCore;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Domain.Media;
using Sangam.MemberFamily.Infrastructure.Persistence;

namespace Sangam.MemberFamily.Infrastructure.Repositories;

/// <summary>
/// Keeps hosted images in this service's own database.
/// </summary>
/// <remarks>
/// See <see cref="StoredImage"/> for why here rather than an object store, and
/// <see cref="IImageStore"/> for why that decision sits behind an interface.
/// </remarks>
public sealed class ImageStore(MemberFamilyDbContext dbContext) : IImageStore
{
    public Task<StoredImage?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.StoredImages
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    /// <remarks>
    /// The projection is the point. Selecting the columns rather than the entity
    /// means the bytes never leave Postgres, so answering a conditional request
    /// costs a small index read instead of two megabytes across the wire.
    /// </remarks>
    public Task<ImageDescriptor?> DescribeAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        dbContext.StoredImages
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new ImageDescriptor(
                i.Id, i.ContentType, i.ByteSize, i.ContentHash, i.UploadedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(StoredImage image) => dbContext.StoredImages.Add(image);

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var image = await dbContext.StoredImages
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        // Silent on a miss. A photo replaced twice, or an erasure arriving after
        // somebody already removed their picture, must not fail - and there is
        // nothing for the caller to do differently either way.
        if (image is not null)
        {
            dbContext.StoredImages.Remove(image);
        }
    }

    /// <remarks>
    /// Ignores the query filter because this runs on the erasure consumer, which
    /// has no request and therefore no resolved tenant. The tenant is a
    /// parameter, taken from the event - never from a caller - which is the same
    /// arrangement every other consumer path in this service uses.
    /// </remarks>
    public async Task RemoveAllForOwnerAsync(
        Guid tenantId,
        ImageOwnerKind ownerKind,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var images = await dbContext.StoredImages
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId
                && i.OwnerKind == ownerKind
                && i.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

        if (images.Count > 0)
        {
            dbContext.StoredImages.RemoveRange(images);
        }
    }
}
