using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;

namespace Sangam.IdentityTenant.Application.Users.Queries.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler(
    IUserRepository users,
    ITenantRepository tenants,
    ICurrentUser currentUser)
    : IRequestHandler<GetCurrentUserQuery, Result<CurrentUserResponse>>
{
    public async Task<Result<CurrentUserResponse>> Handle(
        GetCurrentUserQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<CurrentUserResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        // Bypasses the tenant filter, and only this query may. The id is the
        // validated token's subject rather than anything the caller supplied,
        // so it can only return the caller to themselves - and a Super Admin
        // acting on a Samaaj through the gateway override would otherwise be
        // told their own account does not exist, because it lives at the
        // platform tenant and the override names a real one.
        var user = await users.GetSelfAsync(userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<CurrentUserResponse>(
                Error.NotFound("User.NotFound", "This account no longer exists."));
        }

        var tenant = await tenants.GetByIdAsync(user.TenantId, cancellationToken);
        var authorization = await users.GetAuthorizationAsync(user.Id, user.TenantId, cancellationToken);

        return Result.Success(new CurrentUserResponse(
            user.Id,
            user.TenantId,
            tenant?.Slug ?? string.Empty,
            user.MobileOrEmail,
            user.FullName,
            user.Status.ToString(),
            user.IsContactVerified,
            user.LastLoginAt,
            authorization.Roles,
            authorization.Permissions));
    }
}
