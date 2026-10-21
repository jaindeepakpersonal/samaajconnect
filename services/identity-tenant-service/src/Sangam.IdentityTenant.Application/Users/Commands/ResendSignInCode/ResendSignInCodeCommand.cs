using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.ResendSignInCode;

/// <summary>
/// Sends a fresh code for a sign-in waiting on one - the message did not
/// arrive, or took too long. At most one every thirty seconds per challenge.
/// </summary>
/// <remarks>
/// Always answers the same, sent or not: whether a challenge exists, has
/// expired, or was asked about too soon is nothing this caller needs to learn.
/// The challenge still expires when it would have - resending never extends it,
/// so a challenge cannot be kept alive for ever by asking.
/// </remarks>
[AllowAnonymousRequest]
public sealed record ResendSignInCodeCommand(string ChallengeToken) : ICommand<ResendSignInCodeResponse>;

public sealed record ResendSignInCodeResponse(
    /// <summary>
    /// The code itself, echoed back only when
    /// <see cref="SignInSecurityOptions.RevealSecretsInLogs"/> is on - the same
    /// local-compose-only case <see cref="Login.SignInResponse.DevCode"/> covers.
    /// Null (including when no code was actually sent - an unknown or
    /// too-recent challenge answers the same either way).
    /// </summary>
    string? DevCode = null);

public sealed class ResendSignInCodeCommandValidator : AbstractValidator<ResendSignInCodeCommand>
{
    public ResendSignInCodeCommandValidator() => RuleFor(x => x.ChallengeToken).NotEmpty().MaximumLength(100);
}

public sealed class ResendSignInCodeCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ISecurityPolicyResolver securityPolicy,
    SignInSecurityOptions options,
    ILogger<ResendSignInCodeCommandHandler> logger)
    : IRequestHandler<ResendSignInCodeCommand, Result<ResendSignInCodeResponse>>
{
    public async Task<Result<ResendSignInCodeResponse>> Handle(
        ResendSignInCodeCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var user = await users.FindByLoginChallengeAsync(
            passwordHasher.HashDeterministic(command.ChallengeToken), cancellationToken);

        if (user is null || user.Status != UserStatus.Active || user.IsLockedOut(now))
        {
            return Result.Success(new ResendSignInCodeResponse());
        }

        var policy = await securityPolicy.ResolveAsync(user.TenantId, cancellationToken);
        var (otp, code) = LoginOtp.Issue(passwordHasher.Hash, now, policy.OtpLifetime);

        string? devCode = null;

        if (user.ResendSecondFactor(otp, code, now))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (options.RevealSecretsInLogs)
            {
                logger.LogWarning(
                    "Sign-in code {SignInCode} for {MobileOrEmail} (revealed: SignInSecurity:RevealSecretsInLogs)",
                    code, user.MobileOrEmail);
                devCode = code;
            }
        }

        return Result.Success(new ResendSignInCodeResponse(devCode));
    }
}
