using MediatR;
using Sangam.IdentityTenant.Api.Extensions;
using Sangam.IdentityTenant.Application.Tenants;
using Sangam.IdentityTenant.Application.Tenants.Commands.ChangeTenantStatus;
using Sangam.IdentityTenant.Application.Tenants.Commands.CreateTenant;
using Sangam.IdentityTenant.Application.Tenants.Queries.GetTenantBySlug;

namespace Sangam.IdentityTenant.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/identity/tenants").WithTags("Tenants");

        group.MapPost("/", async (
                CreateTenantRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateTenantCommand(
                    request.Name,
                    request.Slug,
                    request.Domain,
                    request.ContactPerson,
                    request.ContactEmail,
                    request.EnabledModules);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(tenant =>
                    Results.Created($"/v1/identity/tenants/{tenant.Slug}", tenant));
            })
            .RequireAuthorization()
            .WithName("CreateTenant")
            .WithSummary("Create a Samaaj tenant (Super Admin only).")
            .Produces<TenantResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}/status", async (
                Guid id,
                ChangeStatusRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ChangeTenantStatusCommand(id, request.Status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ChangeTenantStatus")
            .WithSummary("Activate, deactivate or archive a Samaaj (Super Admin only).")
            .Produces<TenantResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{slug}", async (
                string slug,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetTenantBySlugQuery(slug), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("GetTenantBySlug")
            .WithSummary("Resolve a subdomain slug to a Samaaj. Called by the gateway before auth exists.")
            .Produces<TenantSummaryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// Wire format for tenant creation. Separate from the command so the public
    /// contract can evolve independently of the internal one.
    /// </summary>
    public sealed record ChangeStatusRequest(string Status);

    public sealed record CreateTenantRequest(
        string Name,
        string Slug,
        string? Domain,
        string? ContactPerson,
        string? ContactEmail,
        IReadOnlyCollection<string>? EnabledModules);
}
