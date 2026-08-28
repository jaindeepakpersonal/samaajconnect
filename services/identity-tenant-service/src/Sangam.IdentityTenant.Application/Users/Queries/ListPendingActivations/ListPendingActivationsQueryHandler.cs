using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;

namespace Sangam.IdentityTenant.Application.Users.Queries.ListPendingActivations;

public sealed class ListPendingActivationsQueryHandler(
    IUserRepository users,
    IDateTimeProvider clock)
    : IRequestHandler<ListPendingActivationsQuery, Result<IReadOnlyList<PendingActivationResponse>>>
{
    public async Task<Result<IReadOnlyList<PendingActivationResponse>>> Handle(
        ListPendingActivationsQuery query,
        CancellationToken cancellationToken)
    {
        var pending = await users.ListPendingActivationAsync(cancellationToken);
        var now = clock.UtcNow;

        IReadOnlyList<PendingActivationResponse> results = pending
            .Select(user => new PendingActivationResponse(
                user.Id,
                user.FullName,
                user.MobileOrEmail,
                user.CreatedAt,
                // Whether a code is outstanding, never the code itself - it is
                // stored as a hash and cannot be shown again anyway.
                user.ActivationCode?.IsUsable(now) ?? false,
                user.ActivationCode?.ExpiresAt))
            .ToList();

        return Result.Success(results);
    }
}
