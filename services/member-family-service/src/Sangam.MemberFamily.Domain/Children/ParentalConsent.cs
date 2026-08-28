namespace Sangam.MemberFamily.Domain.Children;

/// <summary>
/// The consent a parent gave before a child's record was created.
/// </summary>
/// <remarks>
/// Recorded on the child rather than in a separate log because, unlike a
/// member's own consent, this is not a switch that gets turned on and off: it
/// is the basis on which the record exists at all. Withdrawing it means
/// deleting the child record, which is erasure and a different request.
/// </remarks>
public sealed class ParentalConsent
{
    /// <summary>The member who attested - in practice the family head.</summary>
    public Guid GivenByMemberId { get; private set; }

    /// <summary>Which version of the child notice they were shown.</summary>
    public string NoticeVersion { get; private set; } = null!;

    /// <summary>
    /// The words they agreed to, stored verbatim. Keeping only a version
    /// number would mean reconstructing the wording from source control to
    /// answer "what did they actually agree?".
    /// </summary>
    public string Attestation { get; private set; } = null!;

    public DateTimeOffset GivenAt { get; private set; }

    private ParentalConsent() { }

    public ParentalConsent(Guid givenByMemberId, DateTimeOffset givenAt)
    {
        GivenByMemberId = givenByMemberId;
        NoticeVersion = ChildDataNotice.CurrentVersion;
        Attestation = ChildDataNotice.Attestation;
        GivenAt = givenAt;
    }
}
