using System.Security.Cryptography;
using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Media;

/// <summary>
/// A Samaaj's logo, hosted by the platform.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as member-family-service's <c>StoredImage</c> and for the same
/// reason — <c>LogoUrl</c> was a link a client supplied, so anyone who saw it
/// fetched the image from whatever host it named. The bytes live in this
/// service's own database, behind <c>ILogoStore</c>, so an object store is one
/// implementation and a migration rather than a rewrite.
/// </para>
/// <para>
/// <b>Who may read one is where this differs, and the difference is real rather
/// than an oversight.</b> A member's photo is served only to their Samaaj,
/// because it is a photograph of a person. A Samaaj's logo is served to anyone,
/// because the screen that needs it most is the registration form — where
/// somebody picks which Samaaj they belong to before they have an account at
/// all. <c>ListRegisterableTenantsQuery</c> is anonymous by necessity for
/// exactly that reason, and a logo beside a name it already publishes adds
/// nothing a caller did not have.
/// </para>
/// <para>
/// So this is the one image on the platform that is <b>not</b>
/// authorization-checked per request, and `SECURITY-CHECKLIST.md` says so
/// rather than letting the member-photo tick imply it covers logos. It is an
/// organisation's public mark, the same one that would be on its letterhead,
/// and it reveals nothing about a person. The size cap and the format sniffing
/// still apply — those are about what the platform will store and serve, not
/// about who may see it.
/// </para>
/// <para>
/// <b>Nothing could set a logo before this.</b> <c>LogoUrl</c> had been on
/// <c>Tenant</c> since the first migration and no command ever took one, so the
/// column was null on every row the platform has ever had while the admin
/// wireframe's Create Samaaj screen drew an "Upload Logo" control with nothing
/// behind it. The tracking problem it was documented as having was therefore
/// entirely theoretical — which is worse than harmless, because a security note
/// about something that cannot happen dilutes the ones that matter.
/// </para>
/// </remarks>
public sealed class TenantLogo : AggregateRoot
{
    public Guid Id { get; private set; }

    /// <summary>
    /// The Samaaj this belongs to.
    /// </summary>
    /// <remarks>
    /// Not <c>ITenantScopedEntity</c>, and deliberately: <c>Tenant</c> is not
    /// one either, because it is the row every other entity's TenantId points
    /// at, and filtering it by tenant would make slug resolution impossible.
    /// A logo is read on the anonymous registration path where no tenant is
    /// resolved at all, so a query filter here would hide every logo from the
    /// one screen that needs them.
    /// </remarks>
    public Guid TenantId { get; private set; }

    /// <summary>Sniffed from the bytes, never taken from the upload's header.</summary>
    public string ContentType { get; private set; } = null!;

    public byte[] Bytes { get; private set; } = null!;

    public int ByteSize { get; private set; }

    /// <summary>Lowercase hex SHA-256 of the bytes, used as the strong ETag.</summary>
    public string ContentHash { get; private set; } = null!;

    public Guid UploadedBy { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private TenantLogo() { }   // EF Core

    /// <summary>
    /// Creates a logo from bytes that have already been accepted.
    /// </summary>
    /// <remarks>
    /// Throws when the bytes are not an acceptable image, which is not the
    /// "never throw for expected outcomes" rule being broken: the handler checks
    /// <see cref="ImageContent.Sniff"/> first and answers with a validation
    /// failure, so reaching this means a caller built the aggregate without the
    /// check — a programming error, which is what exceptions are for
    /// (CLAUDE.md §4.1).
    /// </remarks>
    public static TenantLogo Capture(
        Guid tenantId,
        byte[] bytes,
        Guid uploadedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var contentType = ImageContent.Sniff(bytes)
            ?? throw new ArgumentException(
                "These bytes are not an image this platform accepts. "
                + "Check ImageContent.Sniff before constructing a TenantLogo.",
                nameof(bytes));

        return new TenantLogo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContentType = contentType,
            Bytes = bytes,
            ByteSize = bytes.Length,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            UploadedBy = uploadedBy,
            UploadedAt = now,
        };
    }
}
