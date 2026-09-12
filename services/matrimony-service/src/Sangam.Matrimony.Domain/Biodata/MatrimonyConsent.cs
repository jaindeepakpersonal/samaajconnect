namespace Sangam.Matrimony.Domain.Biodata;

/// <summary>
/// Consent to appear in matrimony search, owned by the profile it justifies -
/// modelled on member-family-service's own `ParentalConsent`, adapted for
/// self-consent rather than parent-for-child.
/// </summary>
/// <remarks>
/// No `WithdrawnByMemberId`: unlike a parent withdrawing a child's consent,
/// this is always given and withdrawn by the same person, so a separate
/// field would carry no information <see cref="GivenAt"/> and
/// <see cref="WithdrawnAt"/> do not already have for free.
/// </remarks>
public sealed class MatrimonyConsent
{
    public string NoticeVersion { get; private set; } = null!;

    /// <summary>
    /// The words agreed to, stored verbatim. Keeping only a version number
    /// would mean reconstructing the wording from source control to answer
    /// "what did they actually agree to?".
    /// </summary>
    public string Attestation { get; private set; } = null!;

    public DateTimeOffset GivenAt { get; private set; }

    /// <summary>When withdrawn, or null while it still stands.</summary>
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public bool Stands => WithdrawnAt is null;

    private MatrimonyConsent() { }

    public MatrimonyConsent(DateTimeOffset givenAt)
    {
        NoticeVersion = MatrimonyConsentNotice.CurrentVersion;
        Attestation = MatrimonyConsentNotice.Attestation;
        GivenAt = givenAt;
    }

    /// <summary>Idempotent: the first withdrawal is the one that counts.</summary>
    internal void Withdraw(DateTimeOffset at)
    {
        if (WithdrawnAt is not null)
        {
            return;
        }

        WithdrawnAt = at;
    }

    /// <summary>Regiving after a withdrawal is a fresh consent, dated now.</summary>
    internal void Regive(DateTimeOffset at)
    {
        NoticeVersion = MatrimonyConsentNotice.CurrentVersion;
        Attestation = MatrimonyConsentNotice.Attestation;
        GivenAt = at;
        WithdrawnAt = null;
    }
}
