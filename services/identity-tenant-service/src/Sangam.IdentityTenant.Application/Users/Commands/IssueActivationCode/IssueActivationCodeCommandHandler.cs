using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.IssueActivationCode;

public sealed class IssueActivationCodeCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<IssueActivationCodeCommandHandler> logger)
    : IRequestHandler<IssueActivationCodeCommand, Result<ActivationCodeResponse>>
{
    public async Task<Result<ActivationCodeResponse>> Handle(
        IssueActivationCodeCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } adminId)
        {
            return Result.Failure<ActivationCodeResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<ActivationCodeResponse>(
                Error.NotFound("User.NotFound", "No such account in this Samaaj."));
        }

        // IDOR guard on the write path, re-checked rather than left to the
        // query filter (SECURITY-CHECKLIST.md).
        if (tenantContext.TenantId is { } tenantId && user.TenantId != tenantId)
        {
            return Result.Failure<ActivationCodeResponse>(
                Error.NotFound("User.NotFound", "No such account in this Samaaj."));
        }

        if (user.Status != UserStatus.PendingActivation)
        {
            // Issuing one for an active account would be a password-reset path
            // wearing the wrong name, and this endpoint is not audited as one.
            return Result.Failure<ActivationCodeResponse>(Error.Conflict(
                "User.NotPendingActivation", "This account is not waiting to be activated."));
        }

        // Reusing the password hasher: the stored form of a code has exactly
        // the same requirements as a stored password, and one salted, slow
        // hash in the codebase is easier to keep correct than two.
        var (code, plaintext) = ActivationCode.Issue(
            adminId, passwordHasher.Hash, clock.UtcNow);

        user.AttachActivationCode(code);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Deliberately not logging the code, only that one was issued.
        logger.LogWarning(
            "Activation code issued for {UserId} by {AdminId}", user.Id, adminId);

        return Result.Success(new ActivationCodeResponse(
            user.Id, user.MobileOrEmail, user.FullName, plaintext, code.ExpiresAt));
    }
}
