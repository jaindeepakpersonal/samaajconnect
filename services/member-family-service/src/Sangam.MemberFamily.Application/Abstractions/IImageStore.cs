using Sangam.MemberFamily.Domain.Media;

namespace Sangam.MemberFamily.Application.Abstractions;

/// <summary>
/// Where the bytes of a hosted image live.
/// </summary>
/// <remarks>
/// <para>
/// The implementation keeps them in this service's own Postgres database, and
/// <see cref="StoredImage"/> says why: at this platform's scale an object store
/// would buy nothing and would cost a second place data lives, outside the
/// backup drill that this repository has spent real effort proving.
/// </para>
/// <para>
/// This interface exists so that stops being true cheaply. Swapping to S3 is
/// one implementation and a migration — the handlers, the authorization and the
/// endpoints do not know where bytes come from. It is the same adapter seam as
/// audit-notification-service's notification channel: named, with a real
/// implementation behind it rather than a stub, so the seam is proven rather
/// than aspirational.
/// </para>
/// <para>
/// Nothing here is tenant-aware, deliberately. <see cref="StoredImage"/>
/// implements <c>ITenantScopedEntity</c>, so the global query filter applies to
/// every read and <c>TenantWriteGuard</c> refuses every cross-tenant write —
/// the same protection every other entity in this service gets, rather than a
/// second scheme that a future implementation could forget to copy.
/// </para>
/// </remarks>
public interface IImageStore
{
    /// <summary>
    /// The image with this id, bytes included, or null.
    /// </summary>
    /// <remarks>
    /// Tenant-filtered. A caller asking for another Samaaj's image id gets null
    /// and therefore a 404, which is the answer this platform gives to every
    /// cross-tenant read: a 403 would confirm the id names something real.
    /// </remarks>
    Task<StoredImage?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The image's type, size and hash without its bytes.
    /// </summary>
    /// <remarks>
    /// This is what answers a conditional request. A browser that already holds
    /// the photo sends <c>If-None-Match</c>, and reading two megabytes out of
    /// the database to decide the answer is 304 would defeat the caching it is
    /// there to enable. A directory page asks about a hundred photos at once.
    /// </remarks>
    Task<ImageDescriptor?> DescribeAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(StoredImage image);

    /// <summary>
    /// Removes an image. Silent when the id is unknown: a photo being replaced
    /// twice, or an erasure arriving after a deletion, must not fail.
    /// </summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every image belonging to an owner, whatever the current profile
    /// points at.
    /// </summary>
    /// <remarks>
    /// For erasure. Deleting only the id the profile holds would leave behind
    /// anything a replaced-photo path had orphaned, and "we deleted the one we
    /// knew about" is not what erasure means. Bypasses the tenant filter,
    /// because an erasure arrives on a consumer with no resolved tenant — the
    /// tenant comes from the event, never from a caller.
    /// </remarks>
    Task RemoveAllForOwnerAsync(
        Guid tenantId,
        ImageOwnerKind ownerKind,
        Guid ownerId,
        CancellationToken cancellationToken = default);
}

/// <summary>An image's metadata, without the bytes.</summary>
public sealed record ImageDescriptor(
    Guid Id,
    string ContentType,
    int ByteSize,
    string ContentHash,
    DateTimeOffset UploadedAt);
