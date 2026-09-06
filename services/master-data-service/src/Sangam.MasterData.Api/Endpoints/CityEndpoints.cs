using MediatR;
using Sangam.MasterData.Api.Extensions;
using Sangam.MasterData.Application.Cities;
using Sangam.MasterData.Application.Cities.Commands.CreateCity;
using Sangam.MasterData.Application.Cities.Commands.DeactivateCity;
using Sangam.MasterData.Application.Cities.Commands.UpdateCity;
using Sangam.MasterData.Application.Cities.Queries;

namespace Sangam.MasterData.Api.Endpoints;

public static class CityEndpoints
{
    public static IEndpointRouteBuilder MapCityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/master-data/cities").WithTags("Master data - locations");

        group.MapGet("/", async (
                Guid? countryId, bool includeInactive, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ListCitiesQuery(countryId, includeInactive), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("ListCities")
            .Produces<IReadOnlyList<CityResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCityByIdQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("GetCity")
            .Produces<CityResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateCityRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new CreateCityCommand(request.CountryId, request.Name), cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/master-data/cities/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateCity")
            .Produces<CityResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (
                Guid id, UpdateCityRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateCityCommand(id, request.Name), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateCity")
            .Produces<CityResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new DeactivateCityCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DeactivateCity")
            .Produces<CityResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record CreateCityRequest(Guid CountryId, string Name);

    public sealed record UpdateCityRequest(string Name);
}
