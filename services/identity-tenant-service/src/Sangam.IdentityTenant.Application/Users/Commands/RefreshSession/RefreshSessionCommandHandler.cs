using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Users.Commands.RefreshSession;

public sealed class RefreshSessionCommandHandler(
    ISessionService sessions,
    IUserRepository users,
    ITenantRepository tenants,
    ITokenIssuer tokenIssuer,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RefreshSessionCommand, Result<RefreshSessionResponse>>
{
    public async Task<Result<RefreshSessionResponse>> Handle(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        var outcome = await sessions.ContinueAsync(command.RefreshToken, cancellationToken);

        if (outcome.Session is not { } session)
        {
            // One answer for every refusal. Distinguishing "no such token" from
            // "already used" from "expired" would tell whoever is holding a
            // stolen token which of those it is.
            //
            // Nothing is saved here on purpose: returning a failure rolls this
            // transaction back, so any revocation done on it would be undone.
            // SessionService revokes on its own connection for exactly that
            // reason - see RevokeSessionOutOfBandAsync.
            return Result.Failure<RefreshSessionResponse>(Error.Unauthorized(
                "Session.Invalid", "Please sign in again."));
        }

        var user = await users.GetSelfAsync(session.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<RefreshSessionResponse>(Error.Unauthorized(
                "Session.Invalid", "Please sign in again."));
        }

        // Roles are re-read rather than carried through the session, so a role
        // granted or revoked while a member was signed in takes effect at the
        // next refresh instead of at the next sign-in.
        var authorization = await users.GetAuthorizationAsync(
            user.Id, user.TenantId, cancellationToken);

        Tenant? tenant = user.IsPlatformAdministrator
            ? null
            : await tenants.GetByIdAsync(user.TenantId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var access = tokenIssuer.Issue(
            user.Id, user.TenantId, user.MobileOrEmail, authorization.Roles, authorization.Permissions);

        return Result.Success(new RefreshSessionResponse(
            access.Token,
            access.ExpiresAt,
            session.RefreshToken,
            session.ExpiresAt,
            user.Id,
            user.TenantId,
            tenant?.Slug ?? string.Empty,
            user.FullName,
            authorization.Roles));
    }
}
