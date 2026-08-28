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
    public string? PhotoUrl { get; private set; }
    public ChildStatus Status { get; private set; }

    /// <summary>The member profile created when conversion was approved.</summary>
    public Guid? ConvertedMemberId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private ChildProfile() { }

    public static ChildProfile Create(
        Guid tenantId,
        Guid familyId,
        string fullName,
        DateOnly dateOfBirth,
        Gender gender,
        string? photoUrl,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        return new ChildProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FamilyId = familyId,
            FullName = fullName.Trim(),
            DateOfBirth = dateOfBirth,
            Gender = gender,
            PhotoUrl = string.IsNullOrWhiteSpace(photoUrl) ? null : photoUrl.Trim(),
            Status = ChildStatus.Minor,
            CreatedAt = createdAt,
        };
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
    public void MarkConverted(Guid convertedMemberId)
    {
        Status = ChildStatus.Converted;
        ConvertedMemberId = convertedMemberId;
    }
}
