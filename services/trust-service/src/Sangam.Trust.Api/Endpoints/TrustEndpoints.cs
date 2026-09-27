using MediatR;
using Sangam.Trust.Api.Extensions;
using Sangam.Trust.Application.Members;
using Sangam.Trust.Application.Members.Commands.AddTrustMember;
using Sangam.Trust.Application.Members.Commands.AssignTrustRole;
using Sangam.Trust.Application.Members.Commands.RemoveTrustMember;
using Sangam.Trust.Application.Members.Queries;
using Sangam.Trust.Application.Trusts;
using Sangam.Trust.Application.Trusts.Commands.ChangeTrustStatus;
using Sangam.Trust.Application.Trusts.Commands.CreateTrust;
using Sangam.Trust.Application.Trusts.Commands.RenameTrust;
using Sangam.Trust.Application.Trusts.Queries;
using Sangam.Trust.Application.Trusts.Roles;

namespace Sangam.Trust.Api.Endpoints;

/// <summary>
/// A Samaaj's local Trusts and their rosters. Phase 1: no elections, donations,
/// expenses or payment collection yet - see this service's own CLAUDE.md.
/// </summary>
public static class TrustEndpoints
{
    public static IEndpointRouteBuilder MapTrustEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/trust").WithTags("Trust");

        group.MapGet("/trusts", async (
                Guid? localityId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListTrustsQuery(localityId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListTrusts")
            .WithSummary("This Samaaj's Trusts, optionally narrowed to one locality.")
            .Produces<IReadOnlyList<TrustResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/trusts", async (
                CreateTrustRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateTrustCommand(request.LocalityId, request.Name);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/trust/trusts/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateTrust")
            .WithSummary("Set a Trust up for a locality. Exactly one per (Samaaj, Locality).")
            .Produces<TrustResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/trusts/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetTrustQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetTrust")
            .WithSummary("One Trust.")
            .Produces<TrustResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/trusts/{id:guid}/status", async (
                Guid id,
                ChangeStatusRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ChangeTrustStatusCommand(id, request.Status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ChangeTrustStatus")
            .WithSummary("Activate or deactivate a Trust.")
            .Produces<TrustResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/trusts/{id:guid}", async (
                Guid id,
                RenameTrustRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RenameTrustCommand(id, request.Name), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RenameTrust")
            .WithSummary("Correct a Trust's name. Its locality stays - see RenameTrustCommand.")
            .Produces<TrustResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/trusts/{id:guid}/members", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListTrustMembersQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListTrustMembers")
            .WithSummary("One Trust's roster, active members first.")
            .Produces<IReadOnlyList<TrustMemberResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/trusts/{id:guid}/members", async (
                Guid id,
                AddMemberRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new AddTrustMemberCommand(id, request.MemberId, request.RoleId);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("AddTrustMember")
            .WithSummary("Add a member to a Trust's roster, or reactivate one previously removed.")
            .Produces<TrustMemberResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/trusts/{id:guid}/members/{trustMemberId:guid}/role", async (
                Guid id,
                Guid trustMemberId,
                AssignRoleRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                _ = id; // The Trust is named in the path for readability; the row id already identifies it.

                var result = await sender.Send(
                    new AssignTrustRoleCommand(trustMemberId, request.RoleId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("AssignTrustRole")
            .WithSummary("Move an active trustee into one of the Trust's roles. 409 when every seat is taken.")
            .Produces<TrustMemberResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/trusts/{id:guid}/members/{trustMemberId:guid}", async (
                Guid id,
                Guid trustMemberId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                _ = id;

                var result = await sender.Send(new RemoveTrustMemberCommand(trustMemberId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RemoveTrustMember")
            .WithSummary("Take a member off a Trust's roster. A repeat removal is a no-op.")
            .Produces<TrustMemberResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/trusts/{id:guid}/roles", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListTrustRolesQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListTrustRoles")
            .WithSummary("A Trust's roles - Trustee first - each with its seats and how many hold it.")
            .Produces<IReadOnlyList<TrustRoleResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/trusts/{id:guid}/roles", async (
                Guid id,
                TrustRoleRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new DefineTrustRoleCommand(id, request.Title, request.Seats), cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/trust/trusts/{id}/roles/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("DefineTrustRole")
            .WithSummary("Add a role to a Trust, with the number of seats an election for it fills.")
            .Produces<TrustRoleResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/trusts/{id:guid}/roles/{roleId:guid}", async (
                Guid id,
                Guid roleId,
                TrustRoleRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new UpdateTrustRoleCommand(id, roleId, request.Title, request.Seats), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateTrustRole")
            .WithSummary("Rename or resize a role. Never below its current holders, and never Trustee.")
            .Produces<TrustRoleResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/trusts/{id:guid}/roles/{roleId:guid}", async (
                Guid id,
                Guid roleId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RemoveTrustRoleCommand(id, roleId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RemoveTrustRole")
            .WithSummary("Remove a role nobody holds and no unpublished election is for.")
            .Produces<Guid>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record CreateTrustRequest(string LocalityId, string Name);

    /// <summary>Active or Inactive.</summary>
    public sealed record ChangeStatusRequest(string Status);

    public sealed record RenameTrustRequest(string Name);

    /// <summary>MemberId is a string, not Guid: see AddTrustMemberCommand's own remarks.</summary>
    public sealed record AddMemberRequest(string MemberId, string? RoleId = null);

    public sealed record AssignRoleRequest(Guid RoleId);

    public sealed record TrustRoleRequest(string Title, int Seats);
}
