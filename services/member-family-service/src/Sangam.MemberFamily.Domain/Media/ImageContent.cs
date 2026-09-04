namespace Sangam.MemberFamily.Domain.Media;

/// <summary>
/// What the platform will accept as image bytes, and what it calls them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The declared content type is not consulted.</b> It is a string the
/// uploader chose, and a caller who wants to store something other than an
/// image will simply declare <c>image/jpeg</c>. The type this returns is read
/// out of the bytes, and the bytes are what get served back — so the
/// <c>Content-Type</c> header a viewer's browser acts on is one the platform
/// derived, not one the uploader supplied. That is the whole point of sniffing
/// here rather than trusting the multipart part's header.
/// </para>
/// <para>
/// Three formats, deliberately: JPEG, PNG and WebP. Every browser this platform
/// targets renders all three. SVG is excluded and the exclusion is
/// load-bearing — an SVG is a document that can carry script, so serving one
/// back from the platform's own origin would be stored cross-site scripting
/// with the tracking hole closed and a worse one opened. GIF is excluded
/// because nothing here needs animation and each accepted format is a decoder
/// exposed to attacker-controlled bytes.
/// </para>
/// <para>
/// The size cap is deliberately small. A directory photo is displayed at a few
/// hundred pixels; 2 MB is generous for that and mean enough that the bytes
/// living in the service's own database stays reasonable. See
/// <see cref="StoredImage"/> for why they live there rather than in an object
/// store.
/// </para>
/// <para>
/// <b>What this does not do is scan for malware</b>, and
/// <c>SECURITY-CHECKLIST.md</c> asks for that under "File handling". Sniffing
/// proves the bytes begin like an image; it proves nothing about what a decoder
/// does with the rest of them. Closing that needs a scanner in the deployment
/// rather than a check in a domain type, and it is tracked rather than quietly
/// counted as done here.
/// </para>
/// </remarks>
public static class ImageContent
{
    /// <summary>2 MB. A profile photo, not a photograph library.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Webp = "image/webp";

    /// <summary>What a caller may be told is acceptable, for an error message.</summary>
    public static IReadOnlyList<string> Accepted { get; } = [Jpeg, Png, Webp];

    /// <summary>
    /// The content type these bytes actually are, or null when they are not one
    /// of the three formats — or are empty, or over <see cref="MaxBytes"/>.
    /// </summary>
    public static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
        {
            return null;
        }

        // JPEG: FF D8 FF. Every JPEG starts with the SOI marker followed by a
        // marker byte; the third byte varies by encoder, so it is not checked.
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return Jpeg;
        }

        // PNG: the eight-byte signature. The 0D 0A ... 0A tail is there to catch
        // a transfer that mangled line endings, and checking all eight is free.
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return Png;
        }

        // WebP: "RIFF" ....  "WEBP" - a RIFF container with a WEBP form type at
        // byte 8. The four length bytes between them are not checked; a wrong
        // length makes a broken image, not an unsafe one.
        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E'
            && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return Webp;
        }

        return null;
    }

    /// <summary>Whether these bytes are an image this platform will store.</summary>
    public static bool IsAcceptable(ReadOnlySpan<byte> bytes) => Sniff(bytes) is not null;
}
