using MediatR;
using Sangam.Trust.Api.Extensions;
using Sangam.Trust.Application.Members;
using Sangam.Trust.Application.Members.Commands.AddTrustMember;
using Sangam.Trust.Application.Members.Commands.RemoveTrustMember;
using Sangam.Trust.Application.Members.Commands.UpdateTrustMember;
using Sangam.Trust.Application.Members.Queries;
using Sangam.Trust.Application.Trusts;
using Sangam.Trust.Application.Trusts.Commands.ChangeTrustStatus;
using Sangam.Trust.Application.Trusts.Commands.CreateTrust;
using Sangam.Trust.Application.Trusts.Queries;

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
                var command = new AddTrustMemberCommand(id, request.MemberId, request.PositionTitle);

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

        group.MapPut("/trusts/{id:guid}/members/{trustMemberId:guid}/position", async (
                Guid id,
                Guid trustMemberId,
                UpdatePositionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                _ = id; // The Trust is named in the path for readability; the row id already identifies it.

                var result = await sender.Send(
                    new UpdateTrustMemberCommand(trustMemberId, request.PositionTitle), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateTrustMemberPosition")
            .WithSummary("Change an active trustee's position title.")
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

        return app;
    }

    public sealed record CreateTrustRequest(string LocalityId, string Name);

    /// <summary>Active or Inactive.</summary>
    public sealed record ChangeStatusRequest(string Status);

    /// <summary>MemberId is a string, not Guid: see AddTrustMemberCommand's own remarks.</summary>
    public sealed record AddMemberRequest(string MemberId, string? PositionTitle);

    public sealed record UpdatePositionRequest(string? PositionTitle);
}
