namespace Sangam.IdentityTenant.Domain.Consents;

/// <summary>
/// The notice a member is shown before consenting, and its version.
/// </summary>
/// <remarks>
/// Versioned because DPDP section 6(7) requires a Data Fiduciary to be able to
/// produce the consent it relied on - which means being able to say what the
/// person was actually shown, not what the notice says today. Every
/// <see cref="ConsentRecord"/> stores the version in force when it was made.
///
/// The wording here is a placeholder that needs review by someone qualified in
/// Indian data protection law, along with the language options section 5(3)
/// requires. See docs/product/DPDP-COMPLIANCE.md.
/// </remarks>
public static class ConsentNotice
{
    /// <summary>
    /// Bump this whenever the text below changes in substance. Records carry
    /// it, so an old version stays meaningful after a new one ships.
    /// </summary>
    public const string CurrentVersion = "2026-08-28.1";

    public static IReadOnlyList<ConsentNoticeItem> Items { get; } =
    [
        new(
            ConsentPurpose.Membership,
            "Your membership",
            "We hold your name, contact details and family links so your Samaaj can "
            + "run its membership, and so other members of your Samaaj can find you in "
            + "the directory. You choose which details are visible, field by field.",
            Required: true),

        new(
            ConsentPurpose.Communications,
            "Samaaj communications",
            "We use your contact details to send you announcements, event notices and "
            + "reminders from your Samaaj. You can turn this off at any time without "
            + "losing your membership.",
            Required: false),

        new(
            ConsentPurpose.CrossSamaajDirectory,
            "Visibility to other Samaaj",
            "We show your name and locality to members of other Samaaj on this "
            + "platform. This is off unless you ask for it.",
            Required: false),
    ];
}

public sealed record ConsentNoticeItem(
    ConsentPurpose Purpose,
    string Title,
    string Description,
    bool Required);
