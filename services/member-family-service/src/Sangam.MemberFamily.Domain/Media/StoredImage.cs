using System.Security.Cryptography;
using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Media;

/// <summary>Which kind of profile an image belongs to.</summary>
/// <remarks>
/// Stored rather than inferred from which column points at the row, because the
/// authorization rules for the two are different — a member's photo is seen by
/// their Samaaj, a child's by their parents and the Pathshala teaching them —
/// and a read path should not have to work out which rule applies by looking
/// for a referrer.
/// </remarks>
public enum ImageOwnerKind
{
    Member = 1,
    Child = 2,
}

/// <summary>
/// One image the platform hosts itself: the bytes, what they are, and whose.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the platform hosts these at all.</b> A <c>PhotoUrl</c> pointing at
/// somebody else's host means every member who opens the directory sends their
/// IP address and user agent to that host. On a child's profile that is
/// third-party tracking of children, which DPDP s.9(3) prohibits outright. The
/// URL validation that shipped before this closed the <c>javascript:</c> hole
/// and said plainly that it did not close this one. This closes it: an image is
/// fetched from the platform's own origin, by a caller the platform
/// authenticated, and no third party learns anything.
/// </para>
/// <para>
/// <b>Why the bytes are in this service's own database</b> rather than an object
/// store. The obvious answer is MinIO in compose and S3 in production, and it
/// was rejected for this scale. A Samaaj runs to a few thousand members with one
/// photo each, capped at 2 MB and typically a tenth of that; the numbers do not
/// need an object store. What they would cost is a second place data lives —
/// one that <c>scripts/backup-restore-drill.sh</c> does not dump, so a platform
/// that has spent real effort proving its backups restore would have quietly
/// acquired a store outside them. In the database the images are inside the
/// existing dump, inside the existing tenant query filter, and inside the
/// transaction that writes the profile row.
/// </para>
/// <para>
/// That trade goes the other way at a size this platform does not have, which is
/// why every read and write goes through <c>IImageStore</c>. The seam is the
/// point: swapping to S3 is one implementation and a migration, not a rewrite of
/// the handlers. The same shape as audit-notification-service's notification
/// adapter — see that service's CLAUDE.md.
/// </para>
/// <para>
/// <b>An image is replaced, never edited.</b> Uploading a new photo writes a new
/// row and repoints the profile at it; the previous row is deleted in the same
/// transaction. There is no version history because nobody has asked what
/// somebody's profile picture used to be, and keeping one would mean a member
/// who replaced a photo they regretted had not actually replaced it.
/// </para>
/// </remarks>
public sealed class StoredImage : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    public ImageOwnerKind OwnerKind { get; private set; }

    /// <summary>The member or child this image is of.</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>Sniffed from the bytes, never taken from the upload's header.</summary>
    public string ContentType { get; private set; } = null!;

    public byte[] Bytes { get; private set; } = null!;

    public int ByteSize { get; private set; }

    /// <summary>
    /// Lowercase hex SHA-256 of the bytes, used as the strong ETag.
    /// </summary>
    /// <remarks>
    /// Stored rather than computed per request: a directory page asks for a
    /// hundred of these, and hashing two megabytes to answer a conditional
    /// request that is about to return 304 is the wrong way round. It also
    /// means a client that already has the image never receives the bytes
    /// again, which is most of what makes serving photos from the platform
    /// affordable.
    /// </remarks>
    public string ContentHash { get; private set; } = null!;

    public Guid UploadedBy { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private StoredImage() { }   // EF Core

    /// <summary>
    /// Creates a stored image from bytes that have already been accepted.
    /// </summary>
    /// <remarks>
    /// Throws when the bytes are not an acceptable image. That is deliberate
    /// and is not the "never throw for expected outcomes" rule being broken:
    /// the handler checks <see cref="ImageContent.Sniff"/> first and answers
    /// with a validation failure, so reaching this exception means a caller
    /// constructed the aggregate without the check — a programming error, which
    /// is exactly what exceptions are reserved for (CLAUDE.md §4.1).
    /// </remarks>
    public static StoredImage Capture(
        Guid tenantId,
        ImageOwnerKind ownerKind,
        Guid ownerId,
        byte[] bytes,
        Guid uploadedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var contentType = ImageContent.Sniff(bytes)
            ?? throw new ArgumentException(
                "These bytes are not an image this platform accepts. "
                + "Check ImageContent.Sniff before constructing a StoredImage.",
                nameof(bytes));

        var image = new StoredImage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerKind = ownerKind,
            OwnerId = ownerId,
            ContentType = contentType,
            Bytes = bytes,
            ByteSize = bytes.Length,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            UploadedBy = uploadedBy,
            UploadedAt = now,
        };

        return image;
    }
}
