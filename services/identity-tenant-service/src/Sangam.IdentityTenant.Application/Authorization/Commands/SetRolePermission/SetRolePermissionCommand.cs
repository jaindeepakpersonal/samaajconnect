using FluentValidation;
using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Authorization.Queries.ListRoles;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Authorization;

namespace Sangam.IdentityTenant.Application.Authorization.Commands.SetRolePermission;

/// <summary>
/// Grants or revokes one permission on one role, for the calling Samaaj.
/// </summary>
/// <remarks>
/// This is the weightiest change an administrator can make here. Granting
/// somebody a role says "this person may do what that role does"; this says
/// "what that role does is now different", for everybody who holds it and
/// everybody who ever will.
///
/// So three things guard it, and all three are in
/// <see cref="MatrixEditing"/> or below rather than in the screen:
///
/// <b>It only ever writes an override for the caller's own Samaaj.</b> The
/// platform defaults in <c>AuthorizationCatalog</c> are never edited, so one
/// Samaaj's decision cannot reach another, and a Samaaj that changes nothing
/// keeps tracking the defaults as they change.
///
/// <b>SuperAdmin cannot be edited</b>, because it is the role that has to be
/// able to repair a Samaaj that has locked itself out.
///
/// <b>A Samaaj Admin cannot lose <c>Roles.Manage</c></b> — the one revocation
/// that would leave a Samaaj unable to undo its own change.
///
/// It is gated on <c>Roles.Manage</c> rather than on <c>AdminUsers.Manage</c>,
/// which is the nearest existing key. Inviting an administrator hands somebody
/// an existing bundle of permissions; this redefines the bundle. A Samaaj that
/// wants the first without the second can now withhold it.
/// </remarks>
[RequiresPermission(PermissionKeys.RolesManage)]
public sealed record SetRolePermissionCommand(Guid RoleId, string PermissionKey, bool Granted)
    : ICommand<RoleMatrixResponse>;

public sealed class SetRolePermissionCommandValidator : AbstractValidator<SetRolePermissionCommand>
{
    public SetRolePermissionCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.PermissionKey).NotEmpty().MaximumLength(120);
    }
}

public sealed class SetRolePermissionCommandHandler(
    IAuthorizationCatalogRepository catalog,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<SetRolePermissionCommand, Result<RoleMatrixResponse>>
{
    public async Task<Result<RoleMatrixResponse>> Handle(
        SetRolePermissionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<RoleMatrixResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            // A Super Admin who has not chosen a Samaaj. The matrix is edited
            // per Samaaj, so there is nothing here to edit.
            return Result.Failure<RoleMatrixResponse>(Error.Forbidden(
                "Matrix.NoSamaaj",
                "Choose a Samaaj before changing what its roles may do."));
        }

        var role = AuthorizationCatalog.Roles.FirstOrDefault(r => r.Id == command.RoleId);

        if (role is null)
        {
            return Result.Failure<RoleMatrixResponse>(
                Error.NotFound("Role.NotFound", "No such role."));
        }

        var permission = AuthorizationCatalog.Permissions
            .FirstOrDefault(p => string.Equals(
                p.Key, command.PermissionKey, StringComparison.OrdinalIgnoreCase));

        if (permission is null)
        {
            return Result.Failure<RoleMatrixResponse>(
                Error.NotFound("Permission.NotFound", "No such permission."));
        }

        if (!MatrixEditing.IsEditable(role.Id))
        {
            return Result.Failure<RoleMatrixResponse>(Error.Forbidden(
                "Matrix.RoleNotEditable",
                "SuperAdmin is platform administration and cannot be changed by a Samaaj."));
        }

        if (!command.Granted && MatrixEditing.IsProtected(role.Id, permission.Id))
        {
            return Result.Failure<RoleMatrixResponse>(Error.Conflict(
                "Matrix.Protected",
                "A Samaaj administrator has to keep the ability to change these permissions, "
                + "or nobody in this Samaaj could change them back."));
        }

        var isDefault = AuthorizationCatalog.RolePermissions
            .Any(rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id);

        var existing = await catalog.FindOverrideAsync(
            tenantId, role.Id, permission.Id, cancellationToken);

        var effectiveBefore = existing?.Granted ?? isDefault;

        if (effectiveBefore == command.Granted)
        {
            // Already where it is being asked to go. Success rather than a
            // conflict: a repeated click is not a mistake, and the response
            // carries the matrix either way.
            return Result.Success(await catalog.GetMatrixAsync(tenantId, callerMayEdit: true, cancellationToken));
        }

        var now = clock.UtcNow;

        if (command.Granted == isDefault)
        {
            // Back to the platform default. The override is deleted rather than
            // stored as "same as the default", so a Samaaj that undoes a change
            // resumes tracking that default as it changes instead of being
            // pinned to today's version of it.
            if (existing is not null)
            {
                existing.ReturnToDefault(permission.Key, isDefault, actorId, now);
                catalog.RemoveOverride(existing);
            }
        }
        else if (existing is not null)
        {
            existing.Set(permission.Key, command.Granted, actorId, now);
        }
        else
        {
            catalog.AddOverride(RolePermissionOverride.Create(
                tenantId, role.Id, permission.Id, permission.Key,
                command.Granted, effectiveBefore, actorId, now));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await catalog.GetMatrixAsync(tenantId, callerMayEdit: true, cancellationToken));
    }
}
