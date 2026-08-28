using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Consents;

namespace Sangam.IdentityTenant.Application.Consents.Queries.GetMyData;

public sealed class GetMyDataQueryHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IConsentRepository consents,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetMyDataQuery, Result<MyDataResponse>>
{
    public async Task<Result<MyDataResponse>> Handle(
        GetMyDataQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<MyDataResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var user = await users.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<MyDataResponse>(
                Error.NotFound("User.NotFound", "This account no longer exists."));
        }

        var tenant = await tenants.GetByIdAsync(user.TenantId, cancellationToken);
        var authorization = await users.GetAuthorizationAsync(user.Id, user.TenantId, cancellationToken);
        var history = await consents.ListForUserAsync(user.Id, cancellationToken);

        var account = new MyAccountData(
            user.Id,
            user.TenantId,
            tenant?.Slug ?? string.Empty,
            user.MobileOrEmail,
            user.FullName,
            user.Status.ToString(),
            user.IsContactVerified,
            user.CreatedAt,
            user.LastLoginAt,
            [.. authorization.Roles]);

        // The password hash is not here, and must not be. It is data *about*
        // the person only in the sense that a lock is about a key; exporting it
        // would hand out a credential in the name of transparency.
        return Result.Success(new MyDataResponse(
            clock.UtcNow.ToString("O"),
            "identity-tenant-service",
            account,
            ConsentState.ToHistory(history),
            ConsentState.From(history),
            ConsentNotice.Items
                .Select(item => new ConsentNoticeItemResponse(
                    item.Purpose.ToString(), item.Title, item.Description, item.Required))
                .ToList(),
            // Named so the export is honest about not being the whole picture.
            [
                "member-family-service: your profile, family and children",
                "audit-notification-service: your notifications, and the audit record of actions taken",
            ]));
    }
}
