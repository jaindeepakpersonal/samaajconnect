using MediatR;
using Sangam.MasterData.Api.Extensions;
using Sangam.MasterData.Application.LookupItems;
using Sangam.MasterData.Application.LookupItems.Commands.CreateLookupItem;
using Sangam.MasterData.Application.LookupItems.Commands.DeactivateLookupItem;
using Sangam.MasterData.Application.LookupItems.Commands.UpdateLookupItem;
using Sangam.MasterData.Application.LookupItems.Queries;

namespace Sangam.MasterData.Api.Endpoints;

/// <summary>
/// Country, ProfessionType, Gender, MaritalStatus - the four flat lists behind
/// <c>LookupItem</c>. Thin mapping only (root CLAUDE.md §4.6).
/// </summary>
public static class LookupItemEndpoints
{
    public static IEndpointRouteBuilder MapLookupItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/master-data/lookup-items").WithTags("Master data - lookups");

        group.MapGet("/", async (
                string category,
                bool includeInactive,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ListLookupItemsQuery(category, includeInactive), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("ListLookupItems")
            .WithSummary("One category's list (Country, ProfessionType, Gender, MaritalStatus), active first by sort order.")
            .Produces<IReadOnlyList<LookupItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetLookupItemByIdQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("GetLookupItem")
            .Produces<LookupItemResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
                CreateLookupItemRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new CreateLookupItemCommand(
                    request.Category, request.Code, request.Label, request.SortOrder);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/master-data/lookup-items/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateLookupItem")
            .Produces<LookupItemResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", async (
                Guid id, UpdateLookupItemRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new UpdateLookupItemCommand(id, request.Label, request.SortOrder), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateLookupItem")
            .Produces<LookupItemResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new DeactivateLookupItemCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DeactivateLookupItem")
            .Produces<LookupItemResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record CreateLookupItemRequest(string Category, string Code, string Label, int SortOrder);

    public sealed record UpdateLookupItemRequest(string Label, int SortOrder);
}
