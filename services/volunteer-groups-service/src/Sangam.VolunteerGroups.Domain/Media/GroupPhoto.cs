using System.Security.Cryptography;
using Sangam.VolunteerGroups.Domain.Common;

namespace Sangam.VolunteerGroups.Domain.Media;

/// <summary>
/// A volunteer group's photo - the banner across the top of its page. The
/// platform hosts the bytes, the same way identity-tenant-service hosts a
/// Samaaj's logo and member-family-service a member's photo.
/// </summary>
/// <remarks>
/// <para>
/// Hosting rather than a URL somebody types, for the reason member-family-
/// service gives: every member opening the page would otherwise fetch the
/// picture from whatever host was named, handing it their address. In this
/// service's own database for the reason it gives too - a group has one photo,
/// capped at <see cref="ImageContent.MaxBytes"/>, and an object store would be
/// a second place data lives that the backup drill does not dump.
/// </para>
/// <para>
/// The content type is read from the bytes by <see cref="ImageContent.Sniff"/>,
/// never taken from the upload, which is why <see cref="Capture"/> has no
/// content-type parameter. A group photo is often of people, so it is served
/// only to a signed-in member of the Samaaj, never anonymously the way a logo is.
/// </para>
/// </remarks>
public sealed class GroupPhoto : ITenantScopedEntity
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid GroupId { get; private set; }

    /// <summary>Derived from the bytes. The upload's declared type is never consulted.</summary>
    public string ContentType { get; private set; } = null!;

    public byte[] Bytes { get; private set; } = null!;

    public int ByteSize { get; private set; }

    /// <summary>SHA-256 of the bytes, hex - the ETag a browser revalidates with.</summary>
    public string ContentHash { get; private set; } = null!;

    public Guid UploadedBy { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private GroupPhoto() { }   // EF Core

    public static GroupPhoto Capture(
        Guid tenantId,
        Guid groupId,
        byte[] bytes,
        Guid uploadedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var contentType = ImageContent.Sniff(bytes)
            ?? throw new ArgumentException(
                "These bytes are not an image this platform accepts. "
                + "Check ImageContent.Sniff before constructing a GroupPhoto.",
                nameof(bytes));

        return new GroupPhoto
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GroupId = groupId,
            ContentType = contentType,
            Bytes = bytes,
            ByteSize = bytes.Length,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            UploadedBy = uploadedBy,
            UploadedAt = now,
        };
    }
}
