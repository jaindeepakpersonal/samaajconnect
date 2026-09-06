using MediatR;
using Sangam.MasterData.Api.Extensions;
using Sangam.MasterData.Application.Localities;
using Sangam.MasterData.Application.Localities.Commands.CreateLocality;
using Sangam.MasterData.Application.Localities.Commands.DeactivateLocality;
using Sangam.MasterData.Application.Localities.Commands.UpdateLocality;
using Sangam.MasterData.Application.Localities.Queries;

namespace Sangam.MasterData.Api.Endpoints;

public static class LocalityEndpoints
{
    public static IEndpointRouteBuilder MapLocalityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/master-data/localities").WithTags("Master data - locations");

        group.MapGet("/", async (
                Guid? cityId, bool includeInactive, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ListLocalitiesQuery(cityId, includeInactive), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("ListLocalities")
            .Produces<IReadOnlyList<LocalityResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetLocalityByIdQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("GetLocality")
            .Produces<LocalityResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
                CreateLocalityRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new CreateLocalityCommand(request.CityId, request.Name), cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/master-data/localities/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateLocality")
            .Produces<LocalityResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (
                Guid id, UpdateLocalityRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateLocalityCommand(id, request.Name), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateLocality")
            .Produces<LocalityResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new DeactivateLocalityCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DeactivateLocality")
            .Produces<LocalityResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record CreateLocalityRequest(Guid CityId, string Name);

    public sealed record UpdateLocalityRequest(string Name);
}
