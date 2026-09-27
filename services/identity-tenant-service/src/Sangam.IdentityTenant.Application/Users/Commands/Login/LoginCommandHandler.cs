using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.Login;

public sealed class LoginCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordHasher passwordHasher,
    IFailedLoginRecorder failedLoginRecorder,
    ICaptchaStore captchas,
    ITrustedDeviceRepository trustedDevices,
    ITokenIssuer tokenIssuer,
    ISessionService sessions,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ISecurityPolicyResolver securityPolicy,
    SignInSecurityOptions options,
    ILogger<LoginCommandHandler> logger)
    : IRequestHandler<LoginCommand, Result<SignInResponse>>
{
    public async Task<Result<SignInResponse>> Handle(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        // The CAPTCHA first, before any account is looked up: a script that
        // cannot read the picture learns nothing - not even how long a lookup
        // took - and every attempt costs it a solve. Spent whether right or
        // wrong, on its own connection (ICaptchaStore.TryRedeemAsync).
        if (options.CaptchaRequired
            && (command.CaptchaId is not { } captchaId
                || command.CaptchaAnswer is not { } answer
                || !await captchas.TryRedeemAsync(captchaId, answer, cancellationToken)))
        {
            return Result.Failure<SignInResponse>(Error.Unauthorized(
                "Auth.CaptchaFailed", "The characters did not match the picture. Try the new one."));
        }

        var now = clock.UtcNow;
        var identifier = User.NormalizeIdentifier(command.MobileOrEmail);

        var user = await users.FindForLoginAsync(identifier, cancellationToken);

        if (user is null)
        {
            return Result.Failure<SignInResponse>(SignInSteps.InvalidCredentials());
        }

        if (user.IsLockedOut(now))
        {
            // This does reveal that the account exists. The trade is deliberate:
            // a locked-out member who is told nothing will keep guessing and
            // extend their own lockout, and an attacker who has already
            // triggered a lockout has learned as much from the timing anyway.
            return Result.Failure<SignInResponse>(SignInSteps.LockedOut());
        }

        if (!passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            // Written through its own connection: this handler is about to
            // return a failure, and the transaction around it will roll back.
            await failedLoginRecorder.RecordAsync(user.Id, cancellationToken);

            return Result.Failure<SignInResponse>(SignInSteps.InvalidCredentials());
        }

        var (refusal, tenant) = await SignInSteps.CheckAccountAsync(user, tenants, cancellationToken);

        if (refusal is not null)
        {
            return Result.Failure<SignInResponse>(refusal);
        }

        // A device this account trusts skips the code - never the password.
        var trusted = command.DeviceToken is { Length: > 0 } deviceToken
            ? await trustedDevices.FindUsableAsync(user.Id, passwordHasher.HashDeterministic(deviceToken), cancellationToken)
            : null;

        if (!options.TwoFactorRequired || trusted is not null)
        {
            trusted?.MarkUsed(now);

            var session = await SignInSteps.BeginSessionAsync(
                user, tenant, users, tokenIssuer, sessions, unitOfWork, now, cancellationToken);

            return Result.Success(SignInResponse.SignedIn(session));
        }

        // Everywhere else: hold the sign-in open, and send a code.
        var policy = await securityPolicy.ResolveAsync(user.TenantId, cancellationToken);
        var (challenge, challengeToken) = LoginChallenge.Issue(passwordHasher.HashDeterministic, now, policy.OtpLifetime);
        var (otp, code) = LoginOtp.Issue(passwordHasher.Hash, now, policy.OtpLifetime);

        user.BeginSecondFactor(challenge, otp, code, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (options.RevealSecretsInLogs)
        {
            // Local compose only - see SignInSecurityOptions.
            logger.LogWarning(
                "Sign-in code {SignInCode} for {MobileOrEmail} (revealed: SignInSecurity:RevealSecretsInLogs)",
                code, user.MobileOrEmail);
        }

        return Result.Success(SignInResponse.CodeSent(
            challengeToken, SignInSteps.MaskContact(user.MobileOrEmail), challenge.ExpiresAt));
    }
}
