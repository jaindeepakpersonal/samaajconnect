using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// How sign-in is guarded. Bound from the <c>SignInSecurity</c> section; both
/// guards are on unless configuration switches them off.
/// </summary>
/// <remarks>
/// <para>
/// A switch at all is for the integration tests that sign in for another
/// reason - a session test, an activation test - and would otherwise each have
/// to solve a CAPTCHA and read a code first. <c>SignInSecurityTests</c> runs
/// with both on. The host logs a warning at start-up for either one off.
/// </para>
/// <para>
/// <see cref="RevealSecretsInLogs"/> writes each CAPTCHA answer and sign-in
/// code to the log - for the local compose stack only, where there is no SMS or
/// email provider and the smoke run has to sign in, the same stance as
/// audit-notification-service's <c>NotificationDelivery:Logging:RevealContent</c>.
/// </para>
/// <para>
/// Since 2026-09-27 it also echoes the sign-in and password-reset codes back
/// in the response body itself (each command's own <c>DevCode</c>), for the
/// same local-only reason: a developer's browser has no email client either.
/// The name stayed - this is still the one flag for "no real provider, local
/// dev only" - rather than splitting into a second flag for a second place the
/// same secret now surfaces. Never on outside the compose stack.
/// </para>
/// </remarks>
public sealed class SignInSecurityOptions
{
    public const string SectionName = "SignInSecurity";

    public bool CaptchaRequired { get; set; } = true;

    public bool TwoFactorRequired { get; set; } = true;

    public bool RevealSecretsInLogs { get; set; }
}

/// <summary>Where CAPTCHA challenges are kept between the picture and the answer.</summary>
public interface ICaptchaStore
{
    void Add(CaptchaChallenge challenge);

    /// <summary>
    /// Spends a challenge and says whether its answer matched. On its own
    /// connection, so the spending survives the rollback of a sign-in that then
    /// fails - otherwise a wrong password would hand the CAPTCHA back for
    /// another try, which is the replay single use exists to stop. Spent
    /// whether right or wrong, and false for an unknown, used or expired one.
    /// </summary>
    Task<bool> TryRedeemAsync(Guid id, string answer, CancellationToken cancellationToken = default);

    /// <summary>Deletes challenges that expired over an hour ago. Cheap, and done as new ones are issued.</summary>
    Task PurgeExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>Draws a CAPTCHA's answer as a PNG. Nothing here knows the answer after it returns.</summary>
public interface ICaptchaRenderer
{
    byte[] RenderPng(string answer);
}

public interface ITrustedDeviceRepository
{
    void Add(TrustedDevice device);

    /// <summary>A usable device for this account and token hash, or null.</summary>
    Task<TrustedDevice?> FindUsableAsync(Guid userId, string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Every live device for the account. The caller revokes them.</summary>
    Task<IReadOnlyList<TrustedDevice>> ListLiveForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
