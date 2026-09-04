namespace Sangam.MemberFamily.Domain.Children;

/// <summary>
/// The consent a parent gave before a child's record was created.
/// </summary>
/// <remarks>
/// <para>
/// Recorded on the child rather than in a separate log because, unlike a
/// member's own consent, this is not a switch that gets turned on and off: it
/// is the basis on which the record exists at all. Withdrawing it removes the
/// child's record.
/// </para>
/// <para>
/// <b>The withdrawal is recorded here rather than replacing what is here.</b>
/// A consent that has been withdrawn still has to be able to answer "what was
/// agreed, by whom, and when" - DPDP s.6(7) is about being able to demonstrate
/// consent, and a row that erased its own history could demonstrate nothing.
/// So <see cref="GivenAt"/>, <see cref="NoticeVersion"/> and
/// <see cref="Attestation"/> all survive, and the withdrawal is two more
/// fields beside them.
/// </para>
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

    /// <summary>When it was withdrawn, or null while it still stands.</summary>
    public DateTimeOffset? WithdrawnAt { get; private set; }

    /// <summary>
    /// Who withdrew it. Normally <see cref="GivenByMemberId"/>; it is stored
    /// separately because the platform should be able to say that the person
    /// who withdrew was the person who gave, rather than assume it.
    /// </summary>
    public Guid? WithdrawnByMemberId { get; private set; }

    /// <summary>Whether this consent still justifies holding the record.</summary>
    public bool Stands => WithdrawnAt is null;

    private ParentalConsent() { }

    public ParentalConsent(Guid givenByMemberId, DateTimeOffset givenAt)
    {
        GivenByMemberId = givenByMemberId;
        NoticeVersion = ChildDataNotice.CurrentVersion;
        Attestation = ChildDataNotice.Attestation;
        GivenAt = givenAt;
    }

    /// <summary>
    /// Records that this consent no longer stands. Idempotent: the first
    /// withdrawal is the one that counts, because a later timestamp would move
    /// the moment the record stopped being justified.
    /// </summary>
    internal void Withdraw(Guid withdrawnBy, DateTimeOffset at)
    {
        if (WithdrawnAt is not null)
        {
            return;
        }

        WithdrawnAt = at;
        WithdrawnByMemberId = withdrawnBy;
    }
}
