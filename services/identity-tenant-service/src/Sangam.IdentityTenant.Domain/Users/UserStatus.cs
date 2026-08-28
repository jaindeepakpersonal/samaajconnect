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
}
