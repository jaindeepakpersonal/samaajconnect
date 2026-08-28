using MediatR;
using Sangam.IdentityTenant.Api.Extensions;
using Sangam.IdentityTenant.Application.Users.Commands.ActivateAccount;
using Sangam.IdentityTenant.Application.Users.Commands.IssueActivationCode;
using Sangam.IdentityTenant.Application.Users.Queries.ListPendingActivations;

namespace Sangam.IdentityTenant.Api.Endpoints;

/// <summary>
/// The tail of the adult-child conversion flow: a Samaaj admin hands over a
/// one-time code, and the new member redeems it to set a first password.
/// </summary>
public static class ActivationEndpoints
{
    public static IEndpointRouteBuilder MapActivationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/identity/activations").WithTags("Activation");

        group.MapGet("/pending", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListPendingActivationsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListPendingActivations")
            .WithSummary("Accounts waiting to be activated. Samaaj admins only.")
            .Produces<IReadOnlyList<PendingActivationResponse>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{userId:guid}/code", async (
                Guid userId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new IssueActivationCodeCommand(userId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("IssueActivationCode")
            .WithSummary("Mint a one-time activation code. Returned once; stored only as a hash.")
            .Produces<ActivationCodeResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/redeem", async (
                RedeemRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new ActivateAccountCommand(
                    request.MobileOrEmail, request.Code, request.Password);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("ActivateAccount")
            .WithSummary("Redeem an activation code and set a first password.")
            .Produces<ActivateAccountResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    public sealed record RedeemRequest(string MobileOrEmail, string Code, string Password);
}
