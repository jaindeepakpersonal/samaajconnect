using Sangam.MemberFamily.Domain.Common;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Domain.Children;

/// <summary>
/// A child in a household. Has no login: the family manages the record until
/// the child turns 18 and their conversion to a full member is approved.
/// </summary>
public sealed class ChildProfile : AggregateRoot, ITenantScopedEntity
{
    /// <summary>Age at which a child may become a member in their own right.</summary>
    public const int AdultAge = 18;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FamilyId { get; private set; }

    public string FullName { get; private set; } = null!;
    public DateOnly DateOfBirth { get; private set; }
    public Gender Gender { get; private set; }
    /// <summary>
    /// The <see cref="Media.StoredImage"/> holding this child's photo, or null.
    /// </summary>
    /// <remarks>
    /// This is the field DPDP s.9(3) was actually about. A client-supplied URL
    /// meant every viewer of a child's record - their family, their Pathshala -
    /// fetched the picture from whatever host it named, telling that host a
    /// child's photograph had just been looked at and by which IP. The platform
    /// hosts the bytes now, so no third party is told anything.
    /// </remarks>
    public Guid? PhotoImageId { get; private set; }
    public ChildStatus Status { get; private set; }

    /// <summary>
    /// Why this record is allowed to exist at all (DPDP section 9). Not
    /// nullable in practice - the factory requires it - but the property is
    /// nullable so EF can materialise the owned type.
    /// </summary>
    public ParentalConsent? ParentalConsent { get; private set; }

    /// <summary>The member profile created when conversion was approved.</summary>
    public Guid? ConvertedMemberId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private ChildProfile() { }

    /// <summary>
    /// Creates a child record. <paramref name="consentGivenByMemberId"/> is
    /// required rather than optional: DPDP section 9 makes parental consent
    /// the basis on which this data may be held, so a record without it should
    /// not be constructible.
    /// </summary>
    public static ChildProfile Create(
        Guid tenantId,
        Guid familyId,
        string fullName,
        DateOnly dateOfBirth,
        Gender gender,
        Guid consentGivenByMemberId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        if (consentGivenByMemberId == Guid.Empty)
        {
            throw new ArgumentException(
                "A child record cannot be created without recorded parental consent.",
                nameof(consentGivenByMemberId));
        }

        return new ChildProfile
        {
            ParentalConsent = new ParentalConsent(consentGivenByMemberId, createdAt),
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FamilyId = familyId,
            FullName = fullName.Trim(),
            DateOfBirth = dateOfBirth,
            Gender = gender,
            Status = ChildStatus.Minor,
            CreatedAt = createdAt,
        };
    }

    /// <summary>
    /// Points this record at a newly stored photo, answering the previous image
    /// id so the handler can delete it in the same transaction.
    /// </summary>
    /// <remarks>
    /// No event is raised, unlike <see cref="Members.MemberProfile.SetPhoto"/>.
    /// A member's profile changing is news the Samaaj may act on; a child's
    /// photograph changing is a household matter, and publishing it would put a
    /// record that a child's picture was updated into an append-only audit
    /// table that is deliberately hard to redact.
    /// </remarks>
    public Guid? SetPhoto(Guid imageId)
    {
        var previous = PhotoImageId;
        PhotoImageId = imageId;
        return previous;
    }

    /// <summary>
    /// Removes the photo, answering the image id to delete, or null when there
    /// was none — which is success, not an error.
    /// </summary>
    public Guid? RemovePhoto()
    {
        var previous = PhotoImageId;
        PhotoImageId = null;
        return previous;
    }

    /// <summary>
    /// Whether this child is old enough to become a member in their own right.
    /// </summary>
    /// <remarks>
    /// Derived from the date of birth rather than stored as a third status.
    /// A stored "eligible" would need a nightly job to move children into it,
    /// and would be silently wrong on any day that job did not run. DATA-MODEL.md
    /// lists EligibleForConversion as a status; it is computed here instead, and
    /// the stored status only records the two things that are actually
    /// decisions - Minor, and Converted.
    /// </remarks>
    public bool IsEligibleForConversion(DateOnly today) =>
        Status == ChildStatus.Minor && AgeOn(today) >= AdultAge;

    public int AgeOn(DateOnly today)
    {
        var age = today.Year - DateOfBirth.Year;

        // Not had their birthday yet this year.
        if (DateOfBirth > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    /// <summary>
    /// Records that conversion completed and a login now exists. Called only
    /// after a Samaaj admin approved the request.
    /// </summary>
    /// <summary>
    /// Erases the child from this record. Called when the family head who gave
    /// the parental consent erases their own account: the consent was the
    /// basis on which this data was held, so it cannot outlive them.
    /// </summary>
    public void Erase()
    {
        FullName = "Erased child";
        // The reference only; the handler deletes the bytes in the same
        // transaction. A photograph of a child is the last thing that should
        // survive the withdrawal of the consent it was held under.
        PhotoImageId = null;

        // Kept, because age is what decides eligibility and the row still has
        // to behave; shifted to the first of its year so it is no longer a
        // birthday anyone could be recognised by.
        DateOfBirth = new DateOnly(DateOfBirth.Year, 1, 1);
        Gender = Gender.Unspecified;

        // A converted child has their own account and their own consent, so
        // their status is not this service's to change here - the row that
        // remains is the historical link, not a child record any more.
        if (Status == ChildStatus.Minor)
        {
            Status = ChildStatus.Withdrawn;
        }
    }

    /// <summary>
    /// The parent withdrawing the consent this record exists on (DPDP s.6(4)).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Until this existed, the only way to withdraw a parental consent was
    /// to erase your own account.</b> Section 6(4) requires withdrawing to be as
    /// easy as giving, and giving was one tick beside a notice while withdrawing
    /// meant destroying your own membership, your household, and everything you
    /// had ever written. That is not comparable ease; it is the right made
    /// conditional on giving up unrelated ones.
    /// </para>
    /// <para>
    /// What it does is what erasing does to the same record, because there is
    /// only one right answer to "this data may no longer be held": the name and
    /// the photograph go, the birth year survives shifted to 1 January, and the
    /// row stays because a Pathshala enrolment, a register and an exam result
    /// all point at this id. What it adds is the record of the withdrawal
    /// itself, which erasure does not need - there, the person who could be
    /// asked is gone.
    /// </para>
    /// </remarks>
    public void WithdrawParentalConsent(Guid withdrawnBy, DateTimeOffset at)
    {
        ParentalConsent?.Withdraw(withdrawnBy, at);

        Erase();

        // Carries no name, no date and no photograph: audit-notification-service
        // records payloads verbatim in an append-only table, so an event about a
        // child's data being removed must not itself be a copy of it.
        Raise(new ParentalConsentWithdrawnDomainEvent(Id, TenantId, FamilyId, withdrawnBy, at));
    }

    public void MarkConverted(Guid convertedMemberId)
    {
        Status = ChildStatus.Converted;
        ConvertedMemberId = convertedMemberId;
    }
}
