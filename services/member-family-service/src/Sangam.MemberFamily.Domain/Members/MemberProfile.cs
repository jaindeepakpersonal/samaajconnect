using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Members;

/// <summary>
/// A member's profile in one Samaaj.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is the user id from identity-tenant-service, not a new
/// one (DATA-MODEL.md section 3: "Id (=UserId)"). Two services therefore agree
/// on one identifier for a person without either owning the other's table, and
/// a profile can be found from a token's `sub` claim with no lookup table in
/// between.
/// </remarks>
public sealed class MemberProfile : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    public string FullName { get; private set; } = null!;
    public string? PhotoUrl { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public Gender Gender { get; private set; }
    public string? Mobile { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? Locality { get; private set; }
    public string? Profession { get; private set; }

    public FieldPrivacy Privacy { get; private set; } = FieldPrivacy.Default;

    /// <summary>
    /// Whether this member appears in the Samaaj directory search.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-field privacy cannot express this. A member who marks every field
    /// Private is still in the directory under their name, because a directory
    /// listing <i>is</i> a name — there is no privacy level that removes the
    /// row. The wireframe's profile screen has asked for this since the start.
    /// </para>
    /// <para>
    /// <b>It hides a member from the directory search and from nothing else.</b>
    /// Fetching a profile by id still works, and has to: a volunteer group's
    /// president needs to see who applied, a timeline post has an author, a
    /// family has members. Making an unlisted member unreachable by id would
    /// break those and would be read as an access control, which this is not.
    /// It is the difference between being unlisted and being unreachable.
    /// </para>
    /// <para>
    /// A Samaaj administrator still finds them, because correcting a member's
    /// details is part of the job and a member an administrator cannot find is
    /// a member nobody can help.
    /// </para>
    /// </remarks>
    public bool IsListedInDirectory { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private MemberProfile() { }

    /// <summary>
    /// Creates the profile that follows a registration.
    /// </summary>
    /// <remarks>
    /// Called from the UserRegistered consumer, never from an endpoint. A
    /// member does not create their own profile; registering creates it, and
    /// what they do afterwards is update it.
    /// </remarks>
    public static MemberProfile FromRegistration(
        Guid userId,
        Guid tenantId,
        string fullName,
        string? mobileOrEmail,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var profile = new MemberProfile
        {
            Id = userId,
            TenantId = tenantId,
            FullName = fullName.Trim(),
            Gender = Gender.Unspecified,
            Privacy = FieldPrivacy.Default,
            // Listed by default: a member directory that nobody is in by
            // default is not a directory, and the wireframe draws the checkbox
            // ticked. Opting out is a decision the member makes.
            IsListedInDirectory = true,
            CreatedAt = createdAt,
        };

        // The identifier someone registered with is their first known contact
        // detail; seeding it saves them retyping it, and they can change it.
        if (!string.IsNullOrWhiteSpace(mobileOrEmail))
        {
            if (mobileOrEmail.Contains('@'))
            {
                profile.Email = mobileOrEmail.Trim().ToLowerInvariant();
            }
            else
            {
                profile.Mobile = mobileOrEmail.Trim();
            }
        }

        return profile;
    }

    /// <summary>
    /// Erases the person from this profile, keeping the row so family links do
    /// not dangle (DPDP section 12).
    /// </summary>
    /// <remarks>
    /// Every field a person could be recognised by goes, and the privacy
    /// settings are reset to the most closed value rather than left as they
    /// were - an erased profile should not still be carrying an instruction to
    /// publish anything. No event is raised: this is the *result* of an
    /// erasure announced elsewhere, and re-announcing it would loop.
    /// </summary>
    public void Erase(DateTimeOffset erasedAt)
    {
        FullName = "Erased member";
        PhotoUrl = null;
        DateOfBirth = null;
        Gender = Gender.Unspecified;
        Mobile = null;
        Email = null;
        Address = null;
        Locality = null;
        Profession = null;
        Privacy = new FieldPrivacy(
            PrivacyLevel.Private,
            PrivacyLevel.Private,
            PrivacyLevel.Private,
            PrivacyLevel.Private,
            PrivacyLevel.Private);
        // And out of the directory. An erased profile keeps its row so family
        // links do not dangle, but "Erased member" has no business appearing in
        // a list of people you can look up.
        IsListedInDirectory = false;
        UpdatedAt = erasedAt;
    }

    public void Update(
        string fullName,
        string? photoUrl,
        DateOnly? dateOfBirth,
        Gender gender,
        string? mobile,
        string? email,
        string? address,
        string? locality,
        string? profession,
        FieldPrivacy privacy,
        bool isListedInDirectory,
        DateTimeOffset updatedAt,
        Guid updatedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentNullException.ThrowIfNull(privacy);

        // Which fields changed, recorded before anything is overwritten.
        // SECURITY-CHECKLIST.md asks for a before-state on corrections, and a
        // Samaaj admin correcting a member's details is exactly that: an audit
        // row saying only "profile updated" cannot answer what was changed.
        //
        // Names, never values. The audit service stores payloads verbatim in an
        // append-only table, and a member's previous mobile number or address
        // is personal data that would then be deliberately hard to redact.
        // Knowing which field an administrator touched is what the audit
        // question actually needs.
        var changed = new List<string>();

        Note(changed, nameof(FullName), FullName, fullName.Trim());
        Note(changed, nameof(PhotoUrl), PhotoUrl, Normalize(photoUrl));
        Note(changed, nameof(DateOfBirth), DateOfBirth?.ToString("O"), dateOfBirth?.ToString("O"));
        Note(changed, nameof(Gender), Gender.ToString(), gender.ToString());
        Note(changed, nameof(Mobile), Mobile, Normalize(mobile));
        Note(changed, nameof(Email), Email, Normalize(email)?.ToLowerInvariant());
        Note(changed, nameof(Address), Address, Normalize(address));
        Note(changed, nameof(Locality), Locality, Normalize(locality));
        Note(changed, nameof(Profession), Profession, Normalize(profession));

        if (!Privacy.Equals(privacy))
        {
            changed.Add(nameof(Privacy));
        }

        if (IsListedInDirectory != isListedInDirectory)
        {
            changed.Add(nameof(IsListedInDirectory));
        }

        FullName = fullName.Trim();
        PhotoUrl = Normalize(photoUrl);
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Mobile = Normalize(mobile);
        Email = Normalize(email)?.ToLowerInvariant();
        Address = Normalize(address);
        Locality = Normalize(locality);
        Profession = Normalize(profession);
        Privacy = privacy;
        IsListedInDirectory = isListedInDirectory;
        UpdatedAt = updatedAt;

        Raise(new MemberProfileUpdatedDomainEvent(
            Id, TenantId, FullName, changed, updatedBy, updatedAt));
    }

    private static void Note(List<string> changed, string field, string? before, string? after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changed.Add(field);
        }
    }

    /// <summary>
    /// Whether <paramref name="viewer"/> may see a field at this privacy level.
    /// </summary>
    /// <remarks>
    /// A member always sees their own profile in full, and a Samaaj admin sees
    /// every field in their own Samaaj because correcting a member's details is
    /// part of the job (SERVICES.md). Everyone else is bound by the level.
    /// </remarks>
    public bool IsVisibleTo(PrivacyLevel level, ProfileViewer viewer) =>
        viewer.IsSelf(Id) || viewer.IsSamaajAdmin || level != PrivacyLevel.Private;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Who is looking at a profile.</summary>
public sealed record ProfileViewer(Guid? UserId, bool IsSamaajAdmin)
{
    public bool IsSelf(Guid profileId) => UserId == profileId;
}
