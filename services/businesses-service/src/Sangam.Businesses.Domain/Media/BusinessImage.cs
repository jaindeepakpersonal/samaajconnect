using System.Security.Cryptography;
using Sangam.Businesses.Domain.Common;

namespace Sangam.Businesses.Domain.Media;

/// <summary>
/// A business's photo, or one of its products' - the same bytes-in-our-own-
/// database shape events-service's <c>EventPhoto</c> and every other hosted
/// image on this platform use, and for the same reasons: a URL somebody typed
/// would have every member opening the directory fetch it from whatever host it
/// named, and an object store would be a second place data lives that the
/// backup drill does not dump.
/// </summary>
/// <remarks>
/// One table for both, because both belong to one business and go with it: the
/// foreign key cascades, so erasing an owner's account (which deletes their
/// businesses) takes every image with it without a second sweep. Which picture
/// an image is - the business's or a product's - is recorded on the
/// <c>Business</c> or <c>Product</c> that points at it, not here. The content
/// type is read from the bytes by <see cref="ImageContent.Sniff"/>, never taken
/// from the upload, and an image is served only to a signed-in member of the
/// Samaaj.
/// </remarks>
public sealed class BusinessImage : ITenantScopedEntity
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BusinessId { get; private set; }

    /// <summary>Derived from the bytes. The upload's declared type is never consulted.</summary>
    public string ContentType { get; private set; } = null!;

    public byte[] Bytes { get; private set; } = null!;

    public int ByteSize { get; private set; }

    /// <summary>SHA-256 of the bytes, hex - the ETag a browser revalidates with.</summary>
    public string ContentHash { get; private set; } = null!;

    public Guid UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    private BusinessImage() { }   // EF Core

    public static BusinessImage Capture(
        Guid tenantId,
        Guid businessId,
        byte[] bytes,
        Guid uploadedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var contentType = ImageContent.Sniff(bytes)
            ?? throw new ArgumentException(
                "These bytes are not an image this platform accepts. "
                + "Check ImageContent.Sniff before constructing a BusinessImage.",
                nameof(bytes));

        return new BusinessImage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BusinessId = businessId,
            ContentType = contentType,
            Bytes = bytes,
            ByteSize = bytes.Length,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            UploadedBy = uploadedBy,
            UploadedAt = now,
        };
    }
}
