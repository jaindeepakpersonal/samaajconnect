using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Infrastructure.Security;

/// <summary>
/// The one place "a Samaaj's override, or the platform default" is decided.
/// See <see cref="ISecurityPolicyResolver"/>.
/// </summary>
public sealed class SecurityPolicyResolver(ITenantSecurityPolicyRepository policies)
    : ISecurityPolicyResolver
{
    public async Task<ResolvedSecurityPolicy> ResolveAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Guid.Empty (a platform account - User.PlatformTenantId) simply finds
        // no row here, the same as any other tenant that has customised
        // nothing, and falls through to every platform default below.
        var policy = await policies.FindAsync(tenantId, cancellationToken);

        return new ResolvedSecurityPolicy(
            // Max, not just ?? - a Samaaj's own override may only raise the
            // platform floor, never lower it, and this is the one place every
            // caller trusts, so the invariant holds here even if a stored
            // value were ever somehow less than 10.
            MinPasswordLength: Math.Max(10, policy?.MinPasswordLength ?? 10),
            OtpLifetime: policy?.OtpLifetimeMinutes is { } otpMinutes
                ? TimeSpan.FromMinutes(otpMinutes)
                : LoginOtp.Lifetime,
            MaxFailedLoginAttempts: policy?.MaxFailedLoginAttempts ?? User.MaxFailedAttempts,
            LockoutDuration: policy?.LockoutDurationMinutes is { } lockoutMinutes
                ? TimeSpan.FromMinutes(lockoutMinutes)
                : User.LockoutDuration,
            RefreshTokenLifetime: policy?.RefreshTokenLifetimeDays is { } refreshDays
                ? TimeSpan.FromDays(refreshDays)
                : RefreshToken.Lifetime);
    }
}
