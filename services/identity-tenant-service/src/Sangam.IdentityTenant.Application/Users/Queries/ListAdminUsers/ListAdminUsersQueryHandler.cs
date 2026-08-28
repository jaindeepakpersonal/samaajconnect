using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Authorization;

namespace Sangam.IdentityTenant.Application.Users.Queries.ListAdminUsers;

public sealed class ListAdminUsersQueryHandler(IUserRepository users)
    : IRequestHandler<ListAdminUsersQuery, Result<IReadOnlyList<AdminUserResponse>>>
{
    public async Task<Result<IReadOnlyList<AdminUserResponse>>> Handle(
        ListAdminUsersQuery query,
        CancellationToken cancellationToken)
    {
        var admins = await users.ListWithRolesAsync(
            AuthorizationCatalog.AdminAssignableRoleIds, cancellationToken);

        var roleNames = AuthorizationCatalog.Roles.ToDictionary(r => r.Id, r => r.Name);

        IReadOnlyList<AdminUserResponse> results =
        [
            .. admins.Select(user => new AdminUserResponse(
                user.Id,
                user.FullName,
                user.MobileOrEmail,
                user.Status.ToString(),
                user.LastLoginAt,
                [.. user.Roles
                    .Where(r => roleNames.ContainsKey(r.RoleId))
                    .Select(r => roleNames[r.RoleId])
                    .OrderBy(name => name, StringComparer.Ordinal)]))
        ];

        return Result.Success(results);
    }
}
