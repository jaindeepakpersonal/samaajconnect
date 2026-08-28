namespace Sangam.MemberFamily.Domain.Members;

/// <summary>Who may see one field of a member's profile.</summary>
public enum PrivacyLevel
{
    /// <summary>Only the member themselves, and Samaaj admins.</summary>
    Private = 1,

    /// <summary>Anyone signed in to the same Samaaj.</summary>
    SamaajOnly = 2,

    /// <summary>Anyone at all, including other Samaaj.</summary>
    Public = 3,
}
