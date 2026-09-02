using MediatR;
using Sangam.Boli.Api.Extensions;
using Sangam.Boli.Application.Auctions;
using Sangam.Boli.Application.Auctions.Commands.ManageBoli;
using Sangam.Boli.Application.Auctions.Commands.ManageOccasion;
using Sangam.Boli.Application.Auctions.Commands.PlaceBid;
using Sangam.Boli.Application.Auctions.Queries;

namespace Sangam.Boli.Api.Endpoints;

/// <summary>
/// Thin mapping only (root CLAUDE.md section 4.6): bind, build, send, map.
/// </summary>
public static class BoliEndpoints
{
    public static IEndpointRouteBuilder MapBoliEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/boli").WithTags("Boli");

        MapOccasions(group);
        MapBoli(group);
        MapResults(group);

        return app;
    }

    private static void MapOccasions(RouteGroupBuilder group)
    {
        group.MapGet("/occasions", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListOccasionsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListOccasions")
            .WithSummary("Every occasion this Samaaj has announced or held.")
            .Produces<IReadOnlyList<OccasionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/occasions", async (
                CreateOccasionRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new CreateOccasionCommand(request.Title, request.Description, request.OccasionDate),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CreateOccasion")
            .WithSummary("Announce an occasion.")
            .Produces<OccasionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/occasions/{id:guid}", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetOccasionQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetOccasion")
            .WithSummary("One occasion, its Boli types, and the Boli under it.")
            .Produces<OccasionDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/occasions/{id:guid}/boli-types", async (
                Guid id,
                DefineTypeRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new DefineBoliTypeCommand(id, request.Name, request.Description),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DefineBoliType")
            .WithSummary("Define a type of Boli for this occasion.")
            .Produces<BoliTypeResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/occasions/{id:guid}/status", async (
                Guid id,
                MoveOccasionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new MoveOccasionCommand(id, request.Status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("MoveOccasion")
            .WithSummary("Activate or close an occasion.")
            .Produces<OccasionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/occasions/{id:guid}/boli", async (
                Guid id,
                OpenBoliRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new OpenBoliCommand(
                        id,
                        request.BoliTypeId,
                        request.Title,
                        request.StartAt,
                        request.EndAt,
                        request.StartingAmount,
                        request.MinIncrement,
                        request.EligibilityRule),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("OpenBoli")
            .WithSummary("Open a Boli for bidding under this occasion.")
            .Produces<BoliResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapBoli(RouteGroupBuilder group)
    {
        // Declared before "/boli/{id:guid}" so the literal segment is not
        // shadowed by the route parameter.
        group.MapGet("/boli/active", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetActiveBoliQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetActiveBoli")
            .WithSummary("Every Boli taking bids right now.")
            .Produces<IReadOnlyList<BoliResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/boli/{id:guid}", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetBoliQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetBoli")
            .WithSummary("One Boli, its highest bid and the minimum next bid.")
            .Produces<BoliResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/boli/{id:guid}/bids", async (
                Guid id,
                PlaceBidRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new PlaceBidCommand(id, request.Amount), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("PlaceBid")
            .WithSummary("Place a bid. Being outbid answers 200 with accepted: false.")
            .Produces<PlaceBidResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/boli/{id:guid}/bids", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetBidHistoryQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetBidHistory")
            .WithSummary("Amounts and times, highest first. Never who bid.")
            .Produces<IReadOnlyList<BidResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/boli/{id:guid}/close", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CloseBoliCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CloseBoli")
            .WithSummary("End the bidding. Idempotent.")
            .Produces<BoliResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapResults(RouteGroupBuilder group)
    {
        group.MapGet("/results", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetPublishedResultsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetPublishedResults")
            .WithSummary("Everything this Samaaj has announced, newest first.")
            .Produces<IReadOnlyList<BoliResultResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/results/pending", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListPendingResultsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListPendingBoliResults")
            .WithSummary("Recorded and not yet announced. The publisher's queue.")
            .Produces<IReadOnlyList<PendingResultResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/boli/{id:guid}/result", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RecordResultCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RecordBoliResult")
            .WithSummary("Record who won, from the highest bid. Not yet announced.")
            .Produces<BoliResultResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/boli/{id:guid}/result", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetBoliResultQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetBoliResult")
            .WithSummary("One Boli's result. No winner named until it is published.")
            .Produces<BoliResultResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/boli/{id:guid}/result/publish", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new PublishResultCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("PublishBoliResult")
            .WithSummary("Announce it. Idempotent, and irreversible through this API.")
            .Produces<BoliResultResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    // ---- Request bodies ------------------------------------------------------

    public sealed record CreateOccasionRequest(
        string Title, string? Description, DateOnly OccasionDate);

    public sealed record DefineTypeRequest(string Name, string? Description);

    public sealed record MoveOccasionRequest(string Status);

    /// <summary>
    /// Amounts are in paise, the smallest currency unit, as integers. Money in a
    /// floating-point field accumulates error that shows up as a winning bid a
    /// rupee off what somebody actually offered.
    /// </summary>
    public sealed record OpenBoliRequest(
        Guid BoliTypeId,
        string Title,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        long StartingAmount,
        long MinIncrement,
        string? EligibilityRule);

    public sealed record PlaceBidRequest(long Amount);
}
