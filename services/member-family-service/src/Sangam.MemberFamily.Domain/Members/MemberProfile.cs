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
        DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentNullException.ThrowIfNull(privacy);

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
        UpdatedAt = updatedAt;

        Raise(new MemberProfileUpdatedDomainEvent(Id, TenantId, FullName, updatedAt));
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
