namespace Sangam.MemberFamily.Domain.Members;

/// <summary>
/// A member's marital status, as they give it on My Profile (2026-10-24).
/// </summary>
/// <remarks>
/// A fixed list of the three the user named, an enum rather than a
/// master-data-service <c>LookupItem</c>: matrimony-service decides who may
/// keep a biodata from it (<see cref="IsEligibleForMatrimony"/>), and a list a
/// Samaaj could rename or extend is a list whose meaning to that rule nobody
/// could know. The same reasoning <see cref="Gender"/> follows.
/// </remarks>
public enum MaritalStatus
{
    Unmarried = 1,
    Married = 2,
    Divorced = 3,
}

public static class MaritalStatusRules
{
    /// <summary>
    /// Matrimony is for members who are unmarried or divorced (the user's rule).
    /// A member who has not said is not eligible: it is the one rule that has to
    /// be known, not assumed.
    /// </summary>
    public static bool IsEligibleForMatrimony(MaritalStatus? status) =>
        status is MaritalStatus.Unmarried or MaritalStatus.Divorced;
}
