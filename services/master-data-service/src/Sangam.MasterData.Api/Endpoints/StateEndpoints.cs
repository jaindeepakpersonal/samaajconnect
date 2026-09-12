using MediatR;
using Sangam.MasterData.Api.Extensions;
using Sangam.MasterData.Application.States;
using Sangam.MasterData.Application.States.Commands.CreateState;
using Sangam.MasterData.Application.States.Commands.DeactivateState;
using Sangam.MasterData.Application.States.Commands.UpdateState;
using Sangam.MasterData.Application.States.Queries;

namespace Sangam.MasterData.Api.Endpoints;

public static class StateEndpoints
{
    public static IEndpointRouteBuilder MapStateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/master-data/states").WithTags("Master data - locations");

        group.MapGet("/", async (
                Guid? countryId, bool includeInactive, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ListStatesQuery(countryId, includeInactive), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("ListStates")
            .Produces<IReadOnlyList<StateResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetStateByIdQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("GetState")
            .Produces<StateResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateStateRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new CreateStateCommand(request.CountryId, request.Name), cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/master-data/states/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateState")
            .Produces<StateResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (
                Guid id, UpdateStateRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateStateCommand(id, request.Name), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateState")
            .Produces<StateResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new DeactivateStateCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DeactivateState")
            .Produces<StateResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record CreateStateRequest(Guid CountryId, string Name);

    public sealed record UpdateStateRequest(string Name);
}
