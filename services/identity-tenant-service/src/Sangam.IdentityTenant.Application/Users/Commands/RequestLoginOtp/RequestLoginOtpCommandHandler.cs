using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.RequestLoginOtp;

public sealed class RequestLoginOtpCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<RequestLoginOtpCommand, Result<RequestLoginOtpResponse>>
{
    public async Task<Result<RequestLoginOtpResponse>> Handle(
        RequestLoginOtpCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var identifier = User.NormalizeIdentifier(command.MobileOrEmail);

        var user = await users.FindForLoginAsync(identifier, cancellationToken);

        // Every branch below this point returns the same response. Only a
        // qualifying account actually gets a code - which is also the only
        // observable difference, and it is not observable from here: nothing
        // in the HTTP response says which happened.
        if (user is null || user.Status != UserStatus.Active || user.IsLockedOut(now))
        {
            return Result.Success(new RequestLoginOtpResponse());
        }

        if (!user.IsPlatformAdministrator)
        {
            var tenant = await tenants.GetByIdAsync(user.TenantId, cancellationToken);

            if (tenant is null || tenant.Status != TenantStatus.Active)
            {
                return Result.Success(new RequestLoginOtpResponse());
            }
        }

        var (code, plaintext) = LoginOtp.Issue(passwordHasher.Hash, now);

        user.RequestLoginOtp(code, plaintext, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RequestLoginOtpResponse());
    }
}
