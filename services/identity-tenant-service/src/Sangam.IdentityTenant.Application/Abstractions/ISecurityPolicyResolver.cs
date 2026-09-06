namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// One tenant's security policy, fully resolved: every field is a concrete
/// value, never null - a Samaaj's own override where one exists, the
/// platform default everywhere else. Every call site that used to read a
/// hardcoded constant reads one of these instead.
/// </summary>
public sealed record ResolvedSecurityPolicy(
    int MinPasswordLength,
    TimeSpan OtpLifetime,
    int MaxFailedLoginAttempts,
    TimeSpan LockoutDuration,
    TimeSpan RefreshTokenLifetime);

/// <summary>
/// Resolves a tenant's security policy, substituting the platform default for
/// any field a Samaaj has not overridden. The one place "override ?? default"
/// is decided - every caller gets a value it can use directly.
/// </summary>
public interface ISecurityPolicyResolver
{
    Task<ResolvedSecurityPolicy> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
