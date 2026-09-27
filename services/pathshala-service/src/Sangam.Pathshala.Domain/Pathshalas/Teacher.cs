namespace Sangam.Pathshala.Domain.Pathshalas;

/// <summary>
/// Somebody who teaches at a Pathshala - who need not be a member of the
/// Samaaj at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>A record first, a login second.</b> Pathshalas are often taught by a
/// scholar from outside the Samaaj, so a teacher is their own contact record -
/// name, phone, email, qualification - held here, not a member id pointing into
/// member-family-service (confirmed with the user, 2026-09-26). Before this a
/// teacher could only be a member, assigned by member id, with no details at
/// all.
/// </para>
/// <para>
/// <see cref="UserId"/> links the record to a platform account when the teacher
/// has one: a Samaaj member, or somebody given an account with only the
/// <c>PathshalaTeacher</c> role. That account is what lets them mark registers,
/// record marks and send announcements themselves; a teacher without one is a
/// record, and an administrator does those on their behalf. The link is what
/// "teaches this class" is decided against - see <see cref="Pathshala.Teaches"/>.
/// </para>
/// </remarks>
public sealed class Teacher
{
    public Guid Id { get; private set; }
    public Guid PathshalaId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Qualification { get; private set; }

    /// <summary>The platform account they sign in with, if they have one.</summary>
    public Guid? UserId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    private Teacher() { }   // EF Core

    internal Teacher(
        Guid pathshalaId,
        string fullName,
        string? phone,
        string? email,
        string? qualification,
        Guid? userId,
        DateTimeOffset addedAt)
    {
        Id = Guid.NewGuid();
        PathshalaId = pathshalaId;
        AddedAt = addedAt;
        Change(fullName, phone, email, qualification, userId);
    }

    internal void Change(string fullName, string? phone, string? email, string? qualification, Guid? userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        FullName = fullName.Trim();
        Phone = Blank(phone);
        Email = Blank(email);
        Qualification = Blank(qualification);
        UserId = userId;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
