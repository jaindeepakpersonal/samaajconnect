using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.AssignRole;

public sealed class AssignRoleCommandHandler(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<AssignRoleCommandHandler> logger)
    : IRequestHandler<AssignRoleCommand, Result<AssignRoleResponse>>
{
    public async Task<Result<AssignRoleResponse>> Handle(
        AssignRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<AssignRoleResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var role = AuthorizationCatalog.FindRoleByName(command.Role)!;
        var user = await users.GetByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<AssignRoleResponse>(
                Error.NotFound("User.NotFound", "No account with that id exists in this Samaaj."));
        }

        // The IDOR guard (CLAUDE.md §6). GetByIdAsync goes through the tenant
        // query filter, but a write path re-checks rather than trusting it -
        // and this particular write hands out authority, so it is the last
        // place to rely on a filter being applied.
        if (user.TenantId != tenantContext.TenantId)
        {
            return Result.Failure<AssignRoleResponse>(
                Error.NotFound("User.NotFound", "No account with that id exists in this Samaaj."));
        }

        if (user.Status == UserStatus.Erased)
        {
            return Result.Failure<AssignRoleResponse>(Error.Conflict(
                "User.Erased", "This account has been erased and cannot be given a role."));
        }

        // Removing your own last administrative role locks you out of the
        // screen you are standing on, and in a Samaaj with one admin it locks
        // everybody out. Another admin can still do it, which is the point:
        // it takes two people rather than one mis-click.
        if (!command.Granted
            && user.Id == actorId
            && role.Id == AuthorizationCatalog.RoleIds.SamaajAdmin)
        {
            return Result.Failure<AssignRoleResponse>(Error.Conflict(
                "Role.SelfRevoke",
                "You cannot remove your own Samaaj Admin role. Ask another administrator."));
        }

        var changed = command.Granted
            ? user.GrantRole(role.Id, tenantContext.TenantId, actorId, clock.UtcNow)
            : user.RevokeRole(role.Id, tenantContext.TenantId, actorId, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (changed)
        {
            logger.LogInformation(
                "{Action} role {Role} for {UserId} in Samaaj {TenantId}",
                command.Granted ? "Granted" : "Revoked",
                role.Name,
                user.Id,
                tenantContext.TenantId);
        }

        var names = AuthorizationCatalog.Roles
            .Where(r => user.Roles.Any(ur => ur.RoleId == r.Id))
            .Select(r => r.Name)
            .ToList();

        return Result.Success(
            new AssignRoleResponse(user.Id, role.Name, command.Granted, changed, names));
    }
}
