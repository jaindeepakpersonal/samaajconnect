using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.InviteAdmin;

public sealed class InviteAdminCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<InviteAdminCommandHandler> logger)
    : IRequestHandler<InviteAdminCommand, Result<InviteAdminResponse>>
{
    public async Task<Result<InviteAdminResponse>> Handle(
        InviteAdminCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } adminId)
        {
            return Result.Failure<InviteAdminResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            // A Super Admin signed in with no Samaaj selected. There is no
            // Samaaj to invite into, and guessing one would be worse than
            // saying so.
            return Result.Failure<InviteAdminResponse>(Error.Validation(
                new Dictionary<string, string[]>
                {
                    ["tenant"] =
                    [
                        "Select a Samaaj before inviting an administrator into it.",
                    ],
                }));
        }

        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);

        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            // An invitation into a Samaaj that cannot serve traffic produces a
            // code that cannot be redeemed.
            return Result.Failure<InviteAdminResponse>(Error.Conflict(
                "Tenant.NotActive",
                "This Samaaj is not active, so an invitation to it could not be redeemed."));
        }

        var identifier = User.NormalizeIdentifier(command.MobileOrEmail);

        if (await users.IdentifierExistsAsync(identifier, cancellationToken))
        {
            // Platform-wide uniqueness, so this may be an account in another
            // Samaaj. Saying which would confirm that a given identifier is on
            // the platform to anyone holding an admin account anywhere; adding
            // a role to an existing account is what AssignRoleCommand is for.
            return Result.Failure<InviteAdminResponse>(Error.Conflict(
                "User.IdentifierTaken",
                "That mobile number or email address already has an account. "
                + "Give the existing account the role instead."));
        }

        var roleIds = command.Roles
            .Select(name => AuthorizationCatalog.FindRoleByName(name)!.Id)
            .ToList();

        var invited = User.Invite(
            tenantId,
            identifier,
            command.FullName,
            AuthorizationCatalog.RoleIds.Member,
            roleIds,
            adminId,
            clock.UtcNow);

        var (code, plaintext) = ActivationCode.Issue(adminId, passwordHasher.Hash, clock.UtcNow);

        invited.AttachActivationCode(code);

        users.Add(invited);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The code is deliberately absent from this line: it is the one secret
        // in the flow, and a log file is the last place it should survive.
        logger.LogWarning(
            "Admin {UserId} invited into Samaaj {TenantId} with roles {Roles} by {AdminId}",
            invited.Id,
            tenantId,
            string.Join(", ", command.Roles),
            adminId);

        return Result.Success(new InviteAdminResponse(
            invited.Id,
            invited.FullName,
            invited.MobileOrEmail,
            command.Roles,
            plaintext,
            code.ExpiresAt));
    }
}
