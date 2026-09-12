namespace Sangam.Matrimony.Domain.Biodata;

/// <summary>
/// What a member is shown before opting into matrimony search, mirroring
/// member-family-service's own `ChildDataNotice` exactly - this service owns
/// the matrimony record, so it owns the notice for it, rather than reaching
/// into identity-tenant-service's membership notice, which covers a
/// different purpose entirely.
/// </summary>
public static class MatrimonyConsentNotice
{
    /// <summary>
    /// Bump whenever the text below changes in substance. Every
    /// <see cref="MatrimonyConsent"/> stores the version in force when it was
    /// given, so an old consent still says what the member was shown even
    /// after this text is later revised.
    /// </summary>
    public const string CurrentVersion = "2026-09-12.1";

    public const string Summary =
        "We show your name, age, Gautra, profession, education and locality - "
        + "plus whatever you fill in below - to other members of your own Samaaj "
        + "who search for a match. Your mobile number and email are never part of "
        + "this: those stay governed by your profile's own privacy settings, and "
        + "only become visible the way they already do elsewhere on the platform. "
        + "You can withdraw at any time, which takes your biodata out of search "
        + "immediately; proposals already sent or received are kept, because they "
        + "are a record between you and the other member rather than yours alone.";

    public const string Attestation =
        "I consent to my biodata being shown to other members of my own Samaaj "
        + "for the purpose of finding a match, on the terms described.";
}
