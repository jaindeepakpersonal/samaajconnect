using MediatR;
using Sangam.VolunteerGroups.Api.Extensions;
using Sangam.VolunteerGroups.Application.Groups;
using Sangam.VolunteerGroups.Application.Groups.Commands.ApplyToGroup;
using Sangam.VolunteerGroups.Application.Groups.Commands.AssignRolePosition;
using Sangam.VolunteerGroups.Application.Groups.Commands.ChangeGroupStatus;
using Sangam.VolunteerGroups.Application.Groups.Commands.CreateGroup;
using Sangam.VolunteerGroups.Application.Groups.Commands.DecideApplication;
using Sangam.VolunteerGroups.Application.Groups.Queries.GetApplications;
using Sangam.VolunteerGroups.Application.Groups.Queries.GetGroup;
using Sangam.VolunteerGroups.Application.Groups.Queries.ListGroups;

namespace Sangam.VolunteerGroups.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class GroupEndpoints
{
    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/volunteer-groups").WithTags("Volunteer groups");

        group.MapGet("/groups", async (
                string? status,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListGroupsQuery(status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListGroups")
            .WithSummary("This Samaaj's volunteer groups, each with the asking member's standing.")
            .Produces<IReadOnlyList<GroupResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/groups", async (
                CreateGroupRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateGroupCommand(
                    request.Name, request.Description, request.FocusArea, request.PresidentMemberId);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/volunteer-groups/groups/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateGroup")
            .WithSummary("Create a group and name its president (Samaaj admins).")
            .Produces<GroupResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/groups/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetGroupQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetGroup")
            .WithSummary("One group with its members.")
            .Produces<GroupDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/groups/{id:guid}/status", async (
                Guid id,
                ChangeStatusRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ChangeGroupStatusCommand(id, request.Status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ChangeGroupStatus")
            .WithSummary("Activate or deactivate a group. A deactivated one keeps its members.")
            .Produces<GroupResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/groups/{id:guid}/applications", async (
                Guid id,
                ApplyRequest? request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ApplyToGroupCommand(id, request?.Note), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ApplyToGroup")
            .WithSummary("Ask to join. The group's president decides.")
            .Produces<ApplyToGroupResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/groups/{id:guid}/applications", async (
                Guid id,
                bool? pendingOnly,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new GetApplicationsQuery(id, pendingOnly ?? true), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetApplications")
            .WithSummary("The president's review queue for this group.")
            .Produces<IReadOnlyList<GroupApplicationResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/groups/{id:guid}/applications/{applicationId:guid}/decide", async (
                Guid id,
                Guid applicationId,
                DecideRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new DecideApplicationCommand(
                    id, applicationId, request.Accept, request.RolePosition);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DecideApplication")
            .WithSummary("Accept or reject an application, optionally giving a position with it.")
            .Produces<GroupApplicationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/groups/{id:guid}/members/{memberId:guid}/position", async (
                Guid id,
                Guid memberId,
                PositionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new AssignRolePositionCommand(id, memberId, request.RolePosition),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("AssignRolePosition")
            .WithSummary("Give a member a position within the group, or clear it.")
            .Produces<GroupDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record CreateGroupRequest(
        string Name,
        string? Description,
        string? FocusArea,
        Guid PresidentMemberId);

    public sealed record ChangeStatusRequest(string Status);

    /// <summary>The note is optional, so the whole body is.</summary>
    public sealed record ApplyRequest(string? Note);

    /// <summary>
    /// <paramref name="Accept"/> is required and has no default: a decision
    /// endpoint whose safest value is implicit is one where a mistyped request
    /// quietly admits somebody.
    /// </summary>
    public sealed record DecideRequest(bool Accept, string? RolePosition);

    /// <summary>A null position clears it.</summary>
    public sealed record PositionRequest(string? RolePosition);
}
