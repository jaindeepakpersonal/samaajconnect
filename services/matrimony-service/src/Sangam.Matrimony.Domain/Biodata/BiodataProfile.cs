using Sangam.Matrimony.Domain.Biodata.Events;
using Sangam.Matrimony.Domain.Common;

namespace Sangam.Matrimony.Domain.Biodata;

/// <summary>
/// A member's matrimony biodata, visible to their own Samaaj once they opt
/// in. <see cref="Id"/> <b>is</b> the member's id, the same "no dedupe
/// table, the id is the identity" convention member-family-service's own
/// `MemberProfile` uses - so "has this member already opted in?" is a
/// primary-key check.
/// </summary>
/// <remarks>
/// <para>
/// The name/gender/date of birth/Gautra/profession/qualification/city/
/// locality fields below are copied once, from the member's own profile, at
/// opt-in time - not linked to it afterwards. A member who later changes
/// their profession on their base profile does not silently change what
/// their biodata search result shows; they edit biodata separately if they
/// want it to match. The same "one-time copy, not a link" rule this
/// platform already applies to a "same as permanent" temporary address.
/// </para>
/// <para>
/// No photo field and no contact fields. Biodata reuses the member's own
/// existing photo (`GET /v1/members/{id}/photo`, already authorized for any
/// same-Samaaj member) and never copies Mobile/Email - those stay behind
/// the base profile's own per-field privacy and are reached, once useful,
/// through the existing member directory.
/// </para>
/// </remarks>
public sealed class BiodataProfile : AggregateRoot, ITenantScopedEntity
{
    /// <summary>The member's own id. There is no separate identifier.</summary>
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    // ---- Copied once from the member's own profile, at opt-in -------------
    public string FullName { get; private set; } = null!;
    public Gender Gender { get; private set; }
    public DateOnly DateOfBirth { get; private set; }
    public Guid? GautraId { get; private set; }
    public Guid? ProfessionId { get; private set; }
    public Guid? QualificationId { get; private set; }
    public Guid? CityId { get; private set; }
    public Guid? LocalityId { get; private set; }

    // ---- Matrimony-specific -------------------------------------------------
    public int? HeightCm { get; private set; }
    public Guid? MaritalStatusId { get; private set; }
    public Diet? Diet { get; private set; }
    public FamilyType? FamilyType { get; private set; }
    public string? FatherOccupation { get; private set; }
    public string? MotherOccupation { get; private set; }
    public int? SiblingsCount { get; private set; }
    public bool? Manglik { get; private set; }
    public string? HoroscopeNotes { get; private set; }
    public string? AboutMe { get; private set; }
    public int? PreferredAgeMin { get; private set; }
    public int? PreferredAgeMax { get; private set; }
    public string? PartnerPreferenceNotes { get; private set; }

    public MatrimonyConsent Consent { get; private set; } = null!;

    /// <summary>
    /// Whether this profile appears in search. True once opted in, false
    /// after withdrawal - gates visibility the same way
    /// `MemberProfile.IsListedInDirectory` gates the member directory.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private BiodataProfile() { }

    /// <summary>
    /// The minimum age to opt in. Computed at the point of opting in, never
    /// stored - the same "eligibility computed, not stored" precedent
    /// member-family-service's own adult-child conversion eligibility
    /// already sets, so nothing needs a nightly job to keep it current.
    /// </summary>
    public const int MinimumAge = 18;

    public static BiodataProfile OptIn(
        Guid memberId,
        Guid tenantId,
        BiodataFields fields,
        bool consentGiven,
        string noticeVersion,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fields.FullName);

        // Enforced unconditionally, not only by the command validator - the
        // same belt-and-braces member-family-service's own `ChildProfile.Create`
        // applies to parental consent, so no future caller can construct a
        // profile without it.
        if (!consentGiven || string.IsNullOrWhiteSpace(noticeVersion))
        {
            throw new ArgumentException(
                "A biodata profile cannot be created without the member's own consent.",
                nameof(consentGiven));
        }

        if (AgeInYearsOn(fields.DateOfBirth, now) < MinimumAge)
        {
            throw new ArgumentException(
                $"A biodata profile requires the member to be at least {MinimumAge}.",
                nameof(fields));
        }

        var profile = new BiodataProfile
        {
            Id = memberId,
            TenantId = tenantId,
            Consent = new MatrimonyConsent(now),
            IsActive = true,
            CreatedAt = now,
        };

        profile.ApplyFields(fields);
        profile.Raise(new BiodataProfileCreatedDomainEvent(profile.Id, tenantId, now));

        return profile;
    }

    /// <summary>
    /// Opting in again after a withdrawal. A fresh consent, dated now, and
    /// every field replaced with what the member submits this time - the
    /// same full-replace shape `MemberProfile.Update` already uses for the
    /// base profile, since a member re-joining is describing themselves
    /// again, not patching a stale row.
    /// </summary>
    public void Rejoin(BiodataFields fields, DateTimeOffset now)
    {
        if (AgeInYearsOn(fields.DateOfBirth, now) < MinimumAge)
        {
            throw new ArgumentException(
                $"A biodata profile requires the member to be at least {MinimumAge}.",
                nameof(fields));
        }

        Consent.Regive(now);
        IsActive = true;
        ApplyFields(fields);
        UpdatedAt = now;
        Raise(new BiodataProfileCreatedDomainEvent(Id, TenantId, now));
    }

    /// <summary>Editing an already-active profile's fields.</summary>
    public void Update(BiodataFields fields, DateTimeOffset now)
    {
        if (AgeInYearsOn(fields.DateOfBirth, now) < MinimumAge)
        {
            throw new ArgumentException(
                $"A biodata profile requires the member to be at least {MinimumAge}.",
                nameof(fields));
        }

        ApplyFields(fields);
        UpdatedAt = now;
    }

    /// <summary>
    /// Deactivates rather than erasing - the opposite of
    /// `ChildProfile.WithdrawParentalConsent`'s destructive de-identify. A
    /// `Proposal` is a joint record between two members, not one member's
    /// own data alone, so withdrawal takes this profile out of search and
    /// blocks new proposals without deleting a record the other party also
    /// has a stake in. Full erasure still happens, unconditionally, when the
    /// member's whole account is erased - see the `identity.user.erased.v1`
    /// consumer.
    /// </summary>
    public void WithdrawConsent(DateTimeOffset now)
    {
        if (!Consent.Stands)
        {
            return;
        }

        Consent.Withdraw(now);
        IsActive = false;
        UpdatedAt = now;
        Raise(new MatrimonyConsentWithdrawnDomainEvent(Id, TenantId, now));
    }

    /// <summary>
    /// Full erasure, called only from the account-erasure consumer. Unlike
    /// <see cref="WithdrawConsent"/>, this removes every field a person could
    /// be recognised by - erasure is a right, withdrawal is a preference,
    /// the same distinction `ChildProfile` draws between the two.
    /// </summary>
    public void Erase(DateTimeOffset now)
    {
        FullName = "Erased member";
        GautraId = null;
        ProfessionId = null;
        QualificationId = null;
        CityId = null;
        LocalityId = null;
        HeightCm = null;
        MaritalStatusId = null;
        Diet = null;
        FamilyType = null;
        FatherOccupation = null;
        MotherOccupation = null;
        SiblingsCount = null;
        Manglik = null;
        HoroscopeNotes = null;
        AboutMe = null;
        PreferredAgeMin = null;
        PreferredAgeMax = null;
        PartnerPreferenceNotes = null;
        IsActive = false;
        UpdatedAt = now;
    }

    private void ApplyFields(BiodataFields fields)
    {
        FullName = fields.FullName.Trim();
        Gender = fields.Gender;
        DateOfBirth = fields.DateOfBirth;
        GautraId = fields.GautraId;
        ProfessionId = fields.ProfessionId;
        QualificationId = fields.QualificationId;
        CityId = fields.CityId;
        LocalityId = fields.LocalityId;
        HeightCm = fields.HeightCm;
        MaritalStatusId = fields.MaritalStatusId;
        Diet = fields.Diet;
        FamilyType = fields.FamilyType;
        FatherOccupation = Normalize(fields.FatherOccupation);
        MotherOccupation = Normalize(fields.MotherOccupation);
        SiblingsCount = fields.SiblingsCount;
        Manglik = fields.Manglik;
        HoroscopeNotes = Normalize(fields.HoroscopeNotes);
        AboutMe = Normalize(fields.AboutMe);
        PreferredAgeMin = fields.PreferredAgeMin;
        PreferredAgeMax = fields.PreferredAgeMax;
        PartnerPreferenceNotes = Normalize(fields.PartnerPreferenceNotes);
    }

    /// <summary>Age in whole years on a given date. Derived, never stored.</summary>
    public static int AgeInYearsOn(DateOnly dateOfBirth, DateTimeOffset on)
    {
        var today = DateOnly.FromDateTime(on.UtcDateTime);
        var age = today.Year - dateOfBirth.Year;

        if (dateOfBirth > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Every field a member submits when opting in, updating, or rejoining -
/// grouped so the three domain methods that need them all share one shape
/// rather than three long parameter lists drifting apart.
/// </summary>
public sealed record BiodataFields(
    string FullName,
    Gender Gender,
    DateOnly DateOfBirth,
    Guid? GautraId,
    Guid? ProfessionId,
    Guid? QualificationId,
    Guid? CityId,
    Guid? LocalityId,
    int? HeightCm,
    Guid? MaritalStatusId,
    Diet? Diet,
    FamilyType? FamilyType,
    string? FatherOccupation,
    string? MotherOccupation,
    int? SiblingsCount,
    bool? Manglik,
    string? HoroscopeNotes,
    string? AboutMe,
    int? PreferredAgeMin,
    int? PreferredAgeMax,
    string? PartnerPreferenceNotes);
