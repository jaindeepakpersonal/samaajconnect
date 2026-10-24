using System.Security.Cryptography;
using Sangam.Competition.Domain.Common;

namespace Sangam.Competition.Domain.Media;

/// <summary>
/// A competition's photo - the cover across the top of its page in both
/// portals (2026-10-24). The platform hosts the bytes, the same way
/// events-service hosts an event's photo (this is that class, copied), and for
/// the same reasons: a URL somebody typed would have every member opening the
/// page fetch it from whatever host it named, and an object store would be a
/// second place data lives that the backup drill does not dump.
/// </summary>
/// <remarks>
/// The content type is read from the bytes by <see cref="ImageContent.Sniff"/>,
/// never taken from the upload. A competition photo is often of people, so it
/// is served only to a signed-in member of the Samaaj, never anonymously.
/// </remarks>
public sealed class CompetitionPhoto : ITenantScopedEntity
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid CompetitionId { get; private set; }

    /// <summary>Derived from the bytes. The upload's declared type is never consulted.</summary>
    public string ContentType { get; private set; } = null!;

    public byte[] Bytes { get; private set; } = null!;

    public int ByteSize { get; private set; }

    /// <summary>SHA-256 of the bytes, hex - the ETag a browser revalidates with.</summary>
    public string ContentHash { get; private set; } = null!;

    public Guid UploadedBy { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private CompetitionPhoto() { }   // EF Core

    public static CompetitionPhoto Capture(
        Guid tenantId,
        Guid competitionId,
        byte[] bytes,
        Guid uploadedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var contentType = ImageContent.Sniff(bytes)
            ?? throw new ArgumentException(
                "These bytes are not an image this platform accepts. "
                + "Check ImageContent.Sniff before constructing an CompetitionPhoto.",
                nameof(bytes));

        return new CompetitionPhoto
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CompetitionId = competitionId,
            ContentType = contentType,
            Bytes = bytes,
            ByteSize = bytes.Length,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            UploadedBy = uploadedBy,
            UploadedAt = now,
        };
    }
}
