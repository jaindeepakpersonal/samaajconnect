using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.RequestPasswordReset;

/// <summary>
/// Mirrors <see cref="RequestLoginOtp.RequestLoginOtpCommandHandler"/>
/// exactly, minting a <see cref="PasswordResetCode"/> instead of a
/// <see cref="LoginOtp"/>.
/// </summary>
public sealed class RequestPasswordResetCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<RequestPasswordResetCommand, Result<RequestPasswordResetResponse>>
{
    public async Task<Result<RequestPasswordResetResponse>> Handle(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var identifier = User.NormalizeIdentifier(command.MobileOrEmail);

        var user = await users.FindForLoginAsync(identifier, cancellationToken);

        if (user is null || user.Status != UserStatus.Active || user.IsLockedOut(now))
        {
            return Result.Success(new RequestPasswordResetResponse());
        }

        if (!user.IsPlatformAdministrator)
        {
            var tenant = await tenants.GetByIdAsync(user.TenantId, cancellationToken);

            if (tenant is null || tenant.Status != TenantStatus.Active)
            {
                return Result.Success(new RequestPasswordResetResponse());
            }
        }

        var (code, plaintext) = PasswordResetCode.Issue(passwordHasher.Hash, now);

        user.RequestPasswordReset(code, plaintext, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RequestPasswordResetResponse());
    }
}
