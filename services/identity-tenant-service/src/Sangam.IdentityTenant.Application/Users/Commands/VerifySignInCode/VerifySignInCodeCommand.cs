using FluentValidation;
using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Application.Users.Commands.Login;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.VerifySignInCode;

/// <summary>
/// The second step of signing in: the code sent after a correct password, with
/// the challenge that password earned. Optionally trusts this device for thirty
/// days, so its next sign-ins skip this step.
/// </summary>
/// <remarks>
/// <para>
/// <b>No identifier, on purpose.</b> The challenge token names the account and
/// can only have come from a correct password a few minutes ago - so this step
/// cannot be reached for an account nobody has the password to, and the code
/// is a second factor rather than an alternative to the first.
/// </para>
/// <para>
/// A wrong code counts against the account's login lockout, like a wrong
/// password (SECURITY-CHECKLIST.md: "any OTP endpoint" is "any endpoint that
/// checks a credential"), through the same <see cref="IFailedLoginRecorder"/>
/// on its own connection. An unknown or expired challenge and a wrong code are
/// one answer.
/// </para>
/// </remarks>
[AllowAnonymousRequest]
public sealed record VerifySignInCodeCommand(string ChallengeToken, string Code, bool TrustDevice)
    : ICommand<SignInResponse>;

public sealed class VerifySignInCodeCommandValidator : AbstractValidator<VerifySignInCodeCommand>
{
    public VerifySignInCodeCommandValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("Enter the 6-digit code.");
    }
}

public sealed class VerifySignInCodeCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordHasher passwordHasher,
    IFailedLoginRecorder failedLoginRecorder,
    ITrustedDeviceRepository trustedDevices,
    ITokenIssuer tokenIssuer,
    ISessionService sessions,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<VerifySignInCodeCommand, Result<SignInResponse>>
{
    private static Result<SignInResponse> InvalidCode() =>
        Result.Failure<SignInResponse>(Error.Unauthorized(
            "Auth.InvalidCode", "That code is not right, or has expired. Check it, or sign in again for a new one."));

    public async Task<Result<SignInResponse>> Handle(VerifySignInCodeCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var user = await users.FindByLoginChallengeAsync(
            passwordHasher.HashDeterministic(command.ChallengeToken), cancellationToken);

        if (user?.LoginChallenge is not { } challenge || !challenge.IsUsable(now))
        {
            return InvalidCode();
        }

        if (user.IsLockedOut(now))
        {
            return Result.Failure<SignInResponse>(SignInSteps.LockedOut());
        }

        if (user.LoginOtp is not { } otp || !otp.IsUsable(now) || !passwordHasher.Verify(command.Code, otp.Hash))
        {
            await failedLoginRecorder.RecordAsync(user.Id, cancellationToken);

            return InvalidCode();
        }

        // Re-checked: the account or its Samaaj may have been suspended in the
        // minutes since the password step.
        var (refusal, tenant) = await SignInSteps.CheckAccountAsync(user, tenants, cancellationToken);

        if (refusal is not null)
        {
            return Result.Failure<SignInResponse>(refusal);
        }

        // Proves the member holds their own contact address - so this is also
        // where IsContactVerified becomes true.
        user.CompleteSecondFactor();

        string? deviceToken = null;
        DateTimeOffset? trustedUntil = null;

        if (command.TrustDevice)
        {
            var (device, plaintext) = TrustedDevice.Issue(user.Id, passwordHasher.HashDeterministic, now);

            trustedDevices.Add(device);
            deviceToken = plaintext;
            trustedUntil = device.ExpiresAt;
        }

        var session = await SignInSteps.BeginSessionAsync(
            user, tenant, users, tokenIssuer, sessions, unitOfWork, now, cancellationToken);

        return Result.Success(SignInResponse.SignedIn(session, deviceToken, trustedUntil));
    }
}
