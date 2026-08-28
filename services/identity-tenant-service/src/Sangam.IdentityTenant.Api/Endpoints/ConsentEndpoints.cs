using MediatR;
using Sangam.IdentityTenant.Api.Extensions;
using Sangam.IdentityTenant.Application.Consents;
using Sangam.IdentityTenant.Application.Consents.Commands.EraseMyAccount;
using Sangam.IdentityTenant.Application.Consents.Commands.WithdrawConsent;
using Sangam.IdentityTenant.Application.Consents.Queries.GetConsentNotice;
using Sangam.IdentityTenant.Application.Consents.Queries.GetMyData;

namespace Sangam.IdentityTenant.Api.Endpoints;

/// <summary>
/// The DPDP surface: the notice, the consents a member has given, and their
/// right to a copy of what is held about them.
/// See docs/product/DPDP-COMPLIANCE.md.
/// </summary>
public static class ConsentEndpoints
{
    public static IEndpointRouteBuilder MapConsentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/identity/consent-notice", async (
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetConsentNoticeQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithTags("Consent")
            .WithName("GetConsentNotice")
            .WithSummary("The consent notice and its version. Anonymous: it is shown before registering.")
            .Produces<ConsentNoticeResponse>();

        var mine = app.MapGroup("/v1/identity/me").WithTags("Consent").RequireAuthorization();

        mine.MapPost("/consents/{purpose}/withdraw", async (
                string purpose,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new WithdrawConsentCommand(purpose), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("WithdrawConsent")
            .WithSummary("Withdraw consent for one purpose. As easy as giving it (DPDP s.6(4)).")
            .Produces<IReadOnlyList<ConsentStateResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        mine.MapPost("/erase", async (
                EraseRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new EraseMyAccountCommand(request.Password), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("EraseMyAccount")
            .WithSummary("Erase your account and your data across the platform (DPDP s.12).")
            .Produces<EraseMyAccountResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        mine.MapGet("/data-export", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMyDataQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("ExportMyData")
            .WithSummary("Everything this service holds about you, and what it is used for (DPDP s.11).")
            .Produces<MyDataResponse>();

        return app;
    }

    /// <summary>
    /// The password proves the person at the keyboard is the account holder,
    /// which is the verification a Fiduciary needs before acting on an
    /// irreversible request.
    /// </summary>
    public sealed record EraseRequest(string Password);
}
