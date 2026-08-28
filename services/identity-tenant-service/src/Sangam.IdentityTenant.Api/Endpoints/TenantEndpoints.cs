using MediatR;
using Sangam.IdentityTenant.Api.Extensions;
using Sangam.IdentityTenant.Application.Tenants;
using Sangam.IdentityTenant.Application.Tenants.Commands.ChangeTenantStatus;
using Sangam.IdentityTenant.Application.Tenants.Commands.CreateTenant;
using Sangam.IdentityTenant.Application.Tenants.Commands.SetGrievanceContact;
using Sangam.IdentityTenant.Application.Tenants.Queries.GetTenantById;
using Sangam.IdentityTenant.Application.Tenants.Queries.GetTenantBySlug;
using Sangam.IdentityTenant.Application.Tenants.Commands.SetTenantModules;
using Sangam.IdentityTenant.Application.Tenants.Queries.ListRegisterableTenants;
using Sangam.IdentityTenant.Application.Tenants.Queries.ListTenants;
using Sangam.IdentityTenant.Domain.Tenants;

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

        group.MapPut("/{id:guid}/grievance-contact", async (
                Guid id,
                GrievanceContactRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new SetGrievanceContactCommand(id, request.Name, request.Email, request.Phone),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("SetGrievanceContact")
            .WithSummary("Name who members complain to about their data (DPDP s.13).")
            .Produces<TenantResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/by-id/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetTenantByIdQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("GetTenantById")
            .WithSummary("Resolve a tenant id to its public summary. Called by the gateway on every authenticated request.")
            .Produces<TenantSummaryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Before "/{slug}", or a request for /v1/identity/tenants/modules would
        // be read as a Samaaj whose slug is "modules".
        group.MapGet("/modules", (CancellationToken _) =>
                Results.Ok(ModuleCatalog.All.Select(m => new
                {
                    key = m.Key,
                    label = m.Label,
                    defaultOn = m.DefaultOn,
                })))
            .AllowAnonymous()
            .WithName("ListModules")
            .WithSummary("The modules a Samaaj can run. Fills the create/edit toggles.");

        group.MapGet("/", async (
                string? status,
                string? search,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ListTenantsQuery(status, search), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListTenants")
            .WithSummary("Every Samaaj, in every status (Super Admin only).")
            .Produces<IReadOnlyList<TenantResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}/modules", async (
                Guid id,
                SetModulesRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new SetTenantModulesCommand(id, request.EnabledModules), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("SetTenantModules")
            .WithSummary("Replace the set of modules a Samaaj runs (Super Admin only).")
            .Produces<TenantResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/directory", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListRegisterableTenantsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("ListRegisterableTenants")
            .WithSummary("Active Samaaj a visitor can register into. Fills the registration picker.")
            .Produces<IReadOnlyList<TenantSummaryResponse>>();

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

    public sealed record GrievanceContactRequest(string? Name, string? Email, string? Phone);

    public sealed record SetModulesRequest(IReadOnlyList<string> EnabledModules);

    public sealed record CreateTenantRequest(
        string Name,
        string Slug,
        string? Domain,
        string? ContactPerson,
        string? ContactEmail,
        IReadOnlyCollection<string>? EnabledModules);
}
