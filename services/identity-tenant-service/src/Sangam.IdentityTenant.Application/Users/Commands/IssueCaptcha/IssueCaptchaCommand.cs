using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.IssueCaptcha;

/// <summary>
/// A fresh CAPTCHA for the sign-in form: an id and a PNG. The answer stays
/// here, hashed. A command rather than a query because it writes the challenge
/// it hands out.
/// </summary>
[AllowAnonymousRequest]
public sealed record IssueCaptchaCommand : ICommand<CaptchaResponse>;

/// <summary>
/// <paramref name="ImagePng"/> is base64, shown as a <c>data:</c> URL - the
/// picture is not a secret and needs no token, unlike a member's photo.
/// </summary>
public sealed record CaptchaResponse(Guid CaptchaId, string ImagePng, DateTimeOffset ExpiresAt);

public sealed class IssueCaptchaCommandHandler(
    ICaptchaStore store,
    ICaptchaRenderer renderer,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    SignInSecurityOptions options,
    ILogger<IssueCaptchaCommandHandler> logger)
    : IRequestHandler<IssueCaptchaCommand, Result<CaptchaResponse>>
{
    public async Task<Result<CaptchaResponse>> Handle(IssueCaptchaCommand command, CancellationToken cancellationToken)
    {
        await store.PurgeExpiredAsync(cancellationToken);

        var (challenge, answer) = CaptchaChallenge.Issue(passwordHasher.HashDeterministic, clock.UtcNow);

        store.Add(challenge);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (options.RevealSecretsInLogs)
        {
            // Local compose only - see SignInSecurityOptions.
            logger.LogWarning("CAPTCHA {CaptchaId} answer {CaptchaAnswer} (revealed: SignInSecurity:RevealSecretsInLogs)",
                challenge.Id, answer);
        }

        return Result.Success(new CaptchaResponse(
            challenge.Id, Convert.ToBase64String(renderer.RenderPng(answer)), challenge.ExpiresAt));
    }
}
