using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.SignOut;

public sealed class SignOutCommandHandler(
    ISessionService sessions,
    IRefreshTokenRepository tokens,
    IPasswordHasher hasher,
    IUnitOfWork unitOfWork,
    ILogger<SignOutCommandHandler> logger)
    : IRequestHandler<SignOutCommand, Result<SignOutResponse>>
{
    public async Task<Result<SignOutResponse>> Handle(
        SignOutCommand command,
        CancellationToken cancellationToken)
    {
        int ended;

        if (command.Everywhere)
        {
            var presented = await tokens.FindByHashAsync(
                hasher.HashDeterministic(command.RefreshToken), cancellationToken);

            // An unknown token identifies nobody, so there is nothing to end
            // everywhere. Reported as success for the same reason as below.
            ended = presented is null
                ? 0
                : await sessions.EndAllForUserAsync(
                    presented.UserId, SessionEndReason.SignedOut, cancellationToken);
        }
        else
        {
            ended = await sessions.EndAsync(
                command.RefreshToken, SessionEndReason.SignedOut, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (ended > 0)
        {
            logger.LogInformation(
                "Signed out: {Count} refresh token(s) revoked (everywhere: {Everywhere})",
                ended,
                command.Everywhere);
        }

        return Result.Success(new SignOutResponse(ended));
    }
}
