using MediatR;
using Sangam.MasterData.Api.Extensions;
using Sangam.MasterData.Application.Gautras;
using Sangam.MasterData.Application.Gautras.Commands.CreateGautra;
using Sangam.MasterData.Application.Gautras.Commands.DeactivateGautra;
using Sangam.MasterData.Application.Gautras.Commands.UpdateGautra;
using Sangam.MasterData.Application.Gautras.Queries;

namespace Sangam.MasterData.Api.Endpoints;

/// <summary>
/// The calling Samaaj's own Gautra list - see <c>ListGautrasQuery</c> for why
/// the reads stay anonymous even though this is the one tenant-scoped
/// aggregate in the service.
/// </summary>
public static class GautraEndpoints
{
    public static IEndpointRouteBuilder MapGautraEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/master-data/gautras").WithTags("Master data - Gautra");

        group.MapGet("/", async (bool includeInactive, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListGautrasQuery(includeInactive), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("ListGautras")
            .WithSummary("This Samaaj's own Gautra list, resolved from X-Tenant-Id.")
            .Produces<IReadOnlyList<GautraResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetGautraByIdQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("GetGautra")
            .Produces<GautraResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateGautraRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateGautraCommand(request.Name), cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/master-data/gautras/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateGautra")
            .WithSummary("Add a Gautra to this Samaaj Admin's own Samaaj.")
            .Produces<GautraResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", async (
                Guid id, UpdateGautraRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateGautraCommand(id, request.Name), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateGautra")
            .Produces<GautraResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new DeactivateGautraCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DeactivateGautra")
            .Produces<GautraResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record CreateGautraRequest(string Name);

    public sealed record UpdateGautraRequest(string Name);
}
