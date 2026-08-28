namespace Sangam.IdentityTenant.Domain.Consents;

/// <summary>
/// What a member is being asked to consent to.
/// </summary>
/// <remarks>
/// Separate purposes, not one flag. DPDP section 6 requires consent to be
/// *specific*, so bundling "run your membership" together with "send you news"
/// into a single tick would make neither valid.
/// </remarks>
public enum ConsentPurpose
{
    /// <summary>
    /// Holding an account and appearing in the Samaaj member directory.
    /// Required: without it there is nothing to run.
    /// </summary>
    Membership = 1,

    /// <summary>Announcements, event notices and other Samaaj communication.</summary>
    Communications = 2,

    /// <summary>
    /// Showing the profile to members of other Samaaj on the platform.
    /// Off unless asked for.
    /// </summary>
    CrossSamaajDirectory = 3,
}

public static class ConsentPurposes
{
    /// <summary>
    /// Purposes registration cannot proceed without. Kept short on purpose:
    /// consent that is a condition of service is only valid where the service
    /// genuinely cannot be provided without it.
    /// </summary>
    public static IReadOnlyCollection<ConsentPurpose> Required { get; } = [ConsentPurpose.Membership];

    public static IReadOnlyCollection<ConsentPurpose> All { get; } =
        Enum.GetValues<ConsentPurpose>();
}
