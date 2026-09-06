namespace Sangam.IdentityTenant.Domain.Tenants;

/// <summary>
/// A Samaaj's own overrides to the platform's security constants - password
/// length, OTP lifetime, lockout, and session length.
/// </summary>
/// <remarks>
/// Sparse by design, the same shape as <c>RolePermissionOverride</c>: a
/// Samaaj that has changed nothing has no row at all and behaves exactly as
/// it did before this existed. Each field is independently nullable - null
/// means "use the platform default for this one", not "use the platform
/// default for everything" - so a Samaaj Admin can tighten lockout without
/// having to also decide about OTP lifetime.
///
/// <see cref="MinPasswordLength"/> can only ever raise the platform floor,
/// never lower it - enforced where the value is resolved
/// (<c>ISecurityPolicyResolver</c>), not here. Every other field can move in
/// either direction: a Samaaj may reasonably want a *shorter* lockout window
/// or a *longer* one, and there is no platform-wide floor those need to
/// respect the way a password does.
/// </remarks>
public sealed class TenantSecurityPolicy
{
    public Guid TenantId { get; private set; }

    public int? MinPasswordLength { get; private set; }
    public int? OtpLifetimeMinutes { get; private set; }
    public int? MaxFailedLoginAttempts { get; private set; }
    public int? LockoutDurationMinutes { get; private set; }
    public int? RefreshTokenLifetimeDays { get; private set; }

    private TenantSecurityPolicy() { } // EF Core

    public static TenantSecurityPolicy Create(Guid tenantId) => new() { TenantId = tenantId };

    /// <summary>Replaces every field at once - one method, the whole shape.</summary>
    public void Update(
        int? minPasswordLength,
        int? otpLifetimeMinutes,
        int? maxFailedLoginAttempts,
        int? lockoutDurationMinutes,
        int? refreshTokenLifetimeDays)
    {
        MinPasswordLength = minPasswordLength;
        OtpLifetimeMinutes = otpLifetimeMinutes;
        MaxFailedLoginAttempts = maxFailedLoginAttempts;
        LockoutDurationMinutes = lockoutDurationMinutes;
        RefreshTokenLifetimeDays = refreshTokenLifetimeDays;
    }
}
