using MediatR;
using Sangam.IdentityTenant.Api.Extensions;
using Sangam.IdentityTenant.Application.Authorization.Commands.SetRolePermission;
using Sangam.IdentityTenant.Application.Authorization.Queries.ListRoles;
using Sangam.IdentityTenant.Application.Users.Commands.AssignRole;
using Sangam.IdentityTenant.Application.Users.Commands.InviteAdmin;
using Sangam.IdentityTenant.Application.Users.Queries.ListAdminUsers;

namespace Sangam.IdentityTenant.Api.Endpoints;

/// <summary>
/// The admin portal's "Admin Users &amp; Roles" screens: who administers this
/// Samaaj, inviting another, and the matrix the backend actually enforces.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/identity").WithTags("Administration");

        group.MapGet("/roles", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListRolesQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListRoles")
            .WithSummary("The role and permission matrix, as the calling Samaaj sees it.")
            .Produces<RoleMatrixResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/roles/{roleId:guid}/permissions/{permissionKey}", async (
                Guid roleId,
                string permissionKey,
                SetRolePermissionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new SetRolePermissionCommand(roleId, permissionKey, request.Granted),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("SetRolePermission")
            .WithSummary("Grant or revoke one permission on one role, for this Samaaj.")
            .Produces<RoleMatrixResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/admins", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListAdminUsersQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListAdminUsers")
            .WithSummary("Everyone administering this Samaaj.")
            .Produces<IReadOnlyList<AdminUserResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/admins", async (
                InviteAdminRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new InviteAdminCommand(
                    request.FullName, request.MobileOrEmail, request.Roles);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(invited =>
                    Results.Created($"/v1/identity/admins/{invited.UserId}", invited));
            })
            .RequireAuthorization()
            .WithName("InviteAdmin")
            .WithSummary("Create an administrator account and issue its one-time activation code.")
            .Produces<InviteAdminResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/admins/{userId:guid}/roles/{role}", async (
                Guid userId,
                string role,
                AssignRoleRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new AssignRoleCommand(userId, role, request.Granted), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("AssignRole")
            .WithSummary("Grant or revoke one role for one account in this Samaaj.")
            .Produces<AssignRoleResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record InviteAdminRequest(
        string FullName,
        string MobileOrEmail,
        IReadOnlyList<string> Roles);

    /// <summary>
    /// A body with one boolean rather than PUT-to-grant and DELETE-to-revoke.
    /// The screen is a checkbox, and one endpoint means one place the checks in
    /// AssignRoleCommandHandler have to be right.
    /// </summary>
    public sealed record AssignRoleRequest(bool Granted);

    /// <summary>Whether the role should carry this permission in this Samaaj.</summary>
    public sealed record SetRolePermissionRequest(bool Granted);
}
