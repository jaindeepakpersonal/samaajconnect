using System.Diagnostics.CodeAnalysis;

namespace Sangam.VolunteerGroups.Domain.Common;

/// <summary>
/// What the platform will accept as a link to a picture.
/// </summary>
/// <remarks>
/// There is no file storage on the platform yet, so a profile or child photo is
/// a URL a client supplies and every viewer's browser then fetches. Length was
/// the only check, and that is not enough for two separate reasons.
///
/// A <c>javascript:</c> or <c>data:</c> URL rendered into a page is stored
/// cross-site scripting. Angular's own sanitiser refuses those in an
/// <c>[src]</c> binding, but the API hands this value to anything that asks and
/// cannot assume its consumer sanitises.
///
/// The one that matters more here: an <c>http(s)</c> URL on somebody else's
/// host means every member who opens the directory sends their IP address and
/// user agent to that host. On a <see cref="Children.ChildProfile"/> that is
/// third-party tracking of children, which DPDP section 9(3) prohibits outright
/// and which this platform satisfies by not doing - see
/// docs/product/DPDP-COMPLIANCE.md.
///
/// So this refuses everything but absolute http(s), which closes the scripting
/// hole. It does <b>not</b> close the tracking one: any host is still allowed.
/// That needs either an allowlist or, properly, the platform hosting its own
/// images - tracked in DEVELOPMENT_PLAN.md. Naming the gap is better than a
/// check that looks complete and is not.
/// </remarks>
public static class ImageUrl
{
    public const int MaxLength = 2048;

    public static bool IsAcceptable([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Absent is fine; not everyone has a photo.
            return true;
        }

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return false;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
