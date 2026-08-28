namespace Sangam.MemberFamily.Domain.Children;

/// <summary>
/// What a parent is told before a child's record is created, and its version.
/// </summary>
/// <remarks>
/// DPDP section 9 requires verifiable parental consent before a child's
/// personal data is processed, and section 5 requires the notice at or before
/// that consent. This service owns child records, so it owns the notice for
/// them - rather than reaching into identity-tenant-service's member notice,
/// which covers a different thing entirely.
///
/// The wording needs review by someone qualified in Indian data protection
/// law, along with what makes consent "verifiable" for a community
/// organisation whose members know each other in person. That question is open
/// in docs/product/DPDP-COMPLIANCE.md.
/// </remarks>
public static class ChildDataNotice
{
    /// <summary>
    /// Bump whenever the text below changes in substance. Every
    /// <see cref="ParentalConsent"/> stores the version in force when it was
    /// given, so an old record still says what the parent was shown.
    /// </summary>
    public const string CurrentVersion = "2026-08-28.1";

    public const string Summary =
        "We hold your child's name, date of birth and gender so your Samaaj can keep a "
        + "family record and, if your Samaaj runs one, enrol them in Pathshala. A child's "
        + "record is visible to your family and to your Samaaj's administrators, and to "
        + "nobody else. We do not track children, monitor their behaviour, or show them "
        + "advertising. When your child turns 18 you can ask for the record to become an "
        + "account of their own, which a Samaaj administrator approves.";

    /// <summary>
    /// What the parent is attesting to. Recorded verbatim on the consent so it
    /// is always possible to say what they agreed, not just that they did.
    /// </summary>
    public const string Attestation =
        "I am this child's parent or lawful guardian, and I consent to their information "
        + "being held for the purposes described.";
}
