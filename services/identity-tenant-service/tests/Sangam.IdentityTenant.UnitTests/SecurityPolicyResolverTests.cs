using FluentAssertions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;
using Sangam.IdentityTenant.Infrastructure.Security;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

/// <summary>
/// The one place "a Samaaj's override, or the platform default" is decided.
/// A tenant with no row at all must resolve to exactly today's hardcoded
/// constants - the zero-risk-rollout guarantee the whole feature depends on.
/// </summary>
public sealed class SecurityPolicyResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ITenantSecurityPolicyRepository _policies =
        Substitute.For<ITenantSecurityPolicyRepository>();

    private readonly SecurityPolicyResolver _resolver;

    public SecurityPolicyResolverTests()
    {
        _resolver = new SecurityPolicyResolver(_policies);
    }

    [Fact]
    public async Task No_row_resolves_to_every_platform_default()
    {
        _policies.FindAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantSecurityPolicy?)null);

        var resolved = await _resolver.ResolveAsync(TenantId);

        resolved.MinPasswordLength.Should().Be(10);
        resolved.OtpLifetime.Should().Be(LoginOtp.Lifetime);
        resolved.MaxFailedLoginAttempts.Should().Be(User.MaxFailedAttempts);
        resolved.LockoutDuration.Should().Be(User.LockoutDuration);
        resolved.RefreshTokenLifetime.Should().Be(RefreshToken.Lifetime);
    }

    [Fact]
    public async Task A_row_with_every_field_null_also_resolves_to_every_default()
    {
        // Distinct from "no row at all" - a Samaaj that set something and then
        // cleared every field back to default should behave identically to
        // one that never touched this screen.
        var policy = TenantSecurityPolicy.Create(TenantId);
        policy.Update(null, null, null, null, null);
        _policies.FindAsync(TenantId, Arg.Any<CancellationToken>()).Returns(policy);

        var resolved = await _resolver.ResolveAsync(TenantId);

        resolved.MinPasswordLength.Should().Be(10);
        resolved.OtpLifetime.Should().Be(LoginOtp.Lifetime);
        resolved.MaxFailedLoginAttempts.Should().Be(User.MaxFailedAttempts);
        resolved.LockoutDuration.Should().Be(User.LockoutDuration);
        resolved.RefreshTokenLifetime.Should().Be(RefreshToken.Lifetime);
    }

    [Fact]
    public async Task Each_field_can_be_overridden_independently()
    {
        var policy = TenantSecurityPolicy.Create(TenantId);
        policy.Update(minPasswordLength: 14, otpLifetimeMinutes: null,
            maxFailedLoginAttempts: 3, lockoutDurationMinutes: null, refreshTokenLifetimeDays: 7);
        _policies.FindAsync(TenantId, Arg.Any<CancellationToken>()).Returns(policy);

        var resolved = await _resolver.ResolveAsync(TenantId);

        resolved.MinPasswordLength.Should().Be(14);
        resolved.OtpLifetime.Should().Be(LoginOtp.Lifetime, "left null - falls back to the default");
        resolved.MaxFailedLoginAttempts.Should().Be(3);
        resolved.LockoutDuration.Should().Be(User.LockoutDuration, "left null - falls back to the default");
        resolved.RefreshTokenLifetime.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task A_stored_value_below_the_platform_floor_is_still_reported_as_the_floor()
    {
        // Belt and braces: the command validator already refuses anything
        // below 10, but the resolver is the one place every caller trusts, so
        // the invariant holds here too even if a row somehow got past that.
        var policy = TenantSecurityPolicy.Create(TenantId);
        policy.Update(minPasswordLength: 4, null, null, null, null);
        _policies.FindAsync(TenantId, Arg.Any<CancellationToken>()).Returns(policy);

        var resolved = await _resolver.ResolveAsync(TenantId);

        resolved.MinPasswordLength.Should().Be(10);
    }
}

public sealed class SecurityPolicyExtensionsTests
{
    private static ResolvedSecurityPolicy Policy(int minPasswordLength) => new(
        minPasswordLength, LoginOtp.Lifetime, User.MaxFailedAttempts, User.LockoutDuration, RefreshToken.Lifetime);

    [Fact]
    public void A_password_meeting_the_minimum_passes()
    {
        Policy(10).ValidatePasswordLength("1234567890").Should().BeNull();
    }

    [Fact]
    public void A_password_shorter_than_a_tenants_stricter_minimum_is_refused()
    {
        var error = Policy(14).ValidatePasswordLength("short-pass"); // 10 chars

        error.Should().NotBeNull();
        error!.FieldErrors.Should().ContainKey("NewPassword");
    }

    [Fact]
    public void The_message_names_this_tenants_own_minimum()
    {
        var error = Policy(16).ValidatePasswordLength("too-short");

        error!.FieldErrors["NewPassword"].Should().ContainSingle(m => m.Contains("16"));
    }
}
