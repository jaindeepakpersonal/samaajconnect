using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.Login;

/// <summary>
/// Common login: the member types one identifier and lands in their own
/// Samaaj. No tenant is supplied, because the identifier is unique
/// platform-wide and therefore already names exactly one Samaaj
/// (member-portal wireframe).
/// </summary>
/// <remarks>
/// <para>
/// <b>The first of two steps since 2026-09-27.</b> The CAPTCHA is checked
/// before anything else, then the password. On a device this account trusts
/// (<paramref name="DeviceToken"/>) that is the whole sign-in; anywhere else a
/// code goes to the account's contact address and the answer is a challenge
/// for <c>VerifySignInCodeCommand</c>, not a session.
/// </para>
/// </remarks>
[AllowAnonymousRequest]
public sealed record LoginCommand(
    string MobileOrEmail,
    string Password,
    Guid? CaptchaId = null,
    string? CaptchaAnswer = null,
    string? DeviceToken = null) : ICommand<SignInResponse>;

/// <summary>
/// Where a sign-in stands: signed in (<see cref="Session"/>), or waiting for the
/// code sent to <see cref="CodeSentTo"/> under <see cref="ChallengeToken"/>.
/// </summary>
public sealed record SignInResponse(
    bool CodeRequired,
    LoginResponse? Session,

    /// <summary>Hand back with the code. Good until <see cref="ChallengeExpiresAt"/>.</summary>
    string? ChallengeToken,

    /// <summary>The contact address the code went to, masked: "ra****@example.com", "******3210".</summary>
    string? CodeSentTo,
    DateTimeOffset? ChallengeExpiresAt,

    /// <summary>Set when the device was just trusted: keep it, and send it with the next sign-in.</summary>
    string? DeviceToken = null,
    DateTimeOffset? DeviceTrustedUntil = null)
{
    public static SignInResponse SignedIn(LoginResponse session, string? deviceToken = null, DateTimeOffset? trustedUntil = null) =>
        new(false, session, null, null, null, deviceToken, trustedUntil);

    public static SignInResponse CodeSent(string challengeToken, string codeSentTo, DateTimeOffset expiresAt) =>
        new(true, null, challengeToken, codeSentTo, expiresAt);
}
