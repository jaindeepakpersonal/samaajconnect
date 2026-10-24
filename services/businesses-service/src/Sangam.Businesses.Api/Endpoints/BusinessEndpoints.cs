using MediatR;
using Sangam.Businesses.Api.Extensions;
using Sangam.Businesses.Application.Businesses;
using Sangam.Businesses.Application.Businesses.Commands.AddProduct;
using Sangam.Businesses.Application.Businesses.Commands.CreateBusiness;
using Sangam.Businesses.Application.Businesses.Commands.SetBusinessStatus;
using Sangam.Businesses.Application.Businesses.Commands.SetProductAvailability;
using Sangam.Businesses.Application.Businesses.Commands.UpdateBusiness;
using Sangam.Businesses.Application.Businesses.Commands.UpdateProduct;
using Sangam.Businesses.Application.Businesses.Queries.GetBusiness;
using Sangam.Businesses.Application.Businesses.Queries.ListMyBusinesses;
using Sangam.Businesses.Application.Businesses.Queries.SearchBusinesses;

namespace Sangam.Businesses.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class BusinessEndpoints
{
    public static IEndpointRouteBuilder MapBusinessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/businesses").WithTags("Businesses");

        group.MapGet("/mine", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListMyBusinessesQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListMyBusinesses")
            .WithSummary("The caller's own businesses, active or not.")
            .Produces<IReadOnlyList<BusinessResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/mine", async (
                CreateBusinessRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new CreateBusinessCommand(
                    request.Name, request.CategoryId, request.CityId, request.LocalityId,
                    request.Description, request.ContactPhone, request.ContactEmail, request.AddressLine);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CreateBusiness")
            .WithSummary("Register a business. Visible in the directory immediately - no moderation.")
            .Produces<BusinessResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPatch("/mine/{businessId:guid}", async (
                Guid businessId, UpdateBusinessRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new UpdateBusinessCommand(
                    businessId, request.Name, request.CategoryId, request.CityId, request.LocalityId,
                    request.Description, request.ContactPhone, request.ContactEmail, request.AddressLine);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateBusiness")
            .WithSummary("Edit a business the caller owns.")
            .Produces<BusinessResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/mine/{businessId:guid}/status", async (
                Guid businessId, SetBusinessStatusRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new SetBusinessStatusCommand(businessId, request.Active), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("SetBusinessStatus")
            .WithSummary("Deactivate or reactivate a business the caller owns. Reversible.")
            .Produces<BusinessResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/mine/{businessId:guid}/products", async (
                Guid businessId, ProductRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new AddProductCommand(businessId, request.Name, request.Description, request.PriceInPaise);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("AddProduct")
            .WithSummary("Add a product under a business the caller owns.")
            .Produces<BusinessResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/mine/{businessId:guid}/products/{productId:guid}", async (
                Guid businessId, Guid productId, ProductRequest request, ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new UpdateProductCommand(
                    businessId, productId, request.Name, request.Description, request.PriceInPaise);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateProduct")
            .WithSummary("Edit a product's name, description or price.")
            .Produces<BusinessResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/mine/{businessId:guid}/products/{productId:guid}/availability", async (
                Guid businessId, Guid productId, SetProductAvailabilityRequest request, ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new SetProductAvailabilityCommand(businessId, productId, request.Available);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("SetProductAvailability")
            .WithSummary("Take a product off sale, or put it back. Never deletes - a past order still names a real product.")
            .Produces<BusinessResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
                Guid? categoryId, Guid? cityId, Guid? localityId, string? term, int? limit,
                ISender sender, CancellationToken cancellationToken) =>
            {
                var query = new SearchBusinessesQuery(categoryId, cityId, localityId, term, limit ?? 50);

                var result = await sender.Send(query, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("SearchBusinesses")
            .WithSummary("The directory: active businesses in the caller's own Samaaj.")
            .Produces<IReadOnlyList<BusinessResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{businessId:guid}", async (
                Guid businessId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetBusinessQuery(businessId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetBusiness")
            .WithSummary("One business's public detail, and its products.")
            .Produces<BusinessResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record CreateBusinessRequest(
        string Name,
        // A raw string, not Guid - see CreateBusinessCommand's own note on
        // why a Guid-typed wire field crashes ASP.NET Core's JSON model
        // binding on malformed or empty input before this validator ever runs.
        string CategoryId,
        string? CityId,
        string? LocalityId,
        string? Description,
        string? ContactPhone,
        string? ContactEmail,
        string? AddressLine);

    public sealed record UpdateBusinessRequest(
        string Name,
        string CategoryId,
        string? CityId,
        string? LocalityId,
        string? Description,
        string? ContactPhone,
        string? ContactEmail,
        string? AddressLine);

    public sealed record SetBusinessStatusRequest(bool Active);

    public sealed record ProductRequest(string Name, string? Description, int PriceInPaise);

    public sealed record SetProductAvailabilityRequest(bool Available);
}
