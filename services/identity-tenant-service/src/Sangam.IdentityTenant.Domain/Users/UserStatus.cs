namespace Sangam.IdentityTenant.Domain.Users;

public enum UserStatus
{
    Active = 1,
    Suspended = 2,

    /// <summary>
    /// The account exists but has no password yet. Created when a Samaaj admin
    /// approves an adult-child conversion: the person is entitled to an
    /// account, but nobody has proved they are the one asking for it until they
    /// redeem an activation code.
    /// </summary>
    PendingActivation = 3,

    /// <summary>
    /// The person exercised their right to erasure (DPDP section 12). The row
    /// survives with every identifying field cleared, because other services
    /// hold rows keyed on this id that must remain joinable to *something* -
    /// deleting it outright would leave dangling references that read as
    /// corruption rather than as a deliberate erasure.
    /// </summary>
    Erased = 4,
}
