using MediatR;
using Sangam.Trust.Api.Extensions;
using Sangam.Trust.Application.Collections;
using Sangam.Trust.Application.Collections.Commands.RecordCollectionPayment;
using Sangam.Trust.Application.Collections.Queries;
using Sangam.Trust.Application.Donations;
using Sangam.Trust.Application.Donations.Commands.MarkDonationCollected;
using Sangam.Trust.Application.Donations.Commands.RecordDonation;
using Sangam.Trust.Application.Donations.Queries;
using Sangam.Trust.Application.Expenses;
using Sangam.Trust.Application.Expenses.Commands.ApproveExpense;
using Sangam.Trust.Application.Expenses.Commands.RecordExpense;
using Sangam.Trust.Application.Expenses.Queries;
using Sangam.Trust.Domain.Collections;
using Sangam.Trust.Domain.Donations;
using Sangam.Trust.Domain.Expenses;

namespace Sangam.Trust.Api.Endpoints;

/// <summary>
/// A Trust's money: donations (pledged, then collected against a receipt
/// number), expenses (recorded, then approved), and the collections ledger -
/// amounts due to the Trust, today opened by a Boli result in its locality.
/// All of it is <c>Trust.Manage</c> except <c>/collections/mine</c>, which is
/// only ever the caller's own.
/// </summary>
public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/trust").WithTags("Trust finance");

        // ---- Donations -------------------------------------------------------

        group.MapGet("/trusts/{id:guid}/donations", async (
                Guid id,
                DonationStatus? status,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListDonationsQuery(id, status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListTrustDonations")
            .WithSummary("A Trust's donations, newest first, optionally only Pledged or Collected.")
            .Produces<IReadOnlyList<DonationResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/trusts/{id:guid}/donations", async (
                Guid id,
                RecordDonationRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new RecordDonationCommand(
                    id, request.DonorMemberId, request.DonorName, request.DonorPhone,
                    request.Amount, request.Purpose, request.Mode);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/trust/donations/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("RecordTrustDonation")
            .WithSummary("Record a pledged donation. It gets a receipt number when it is collected.")
            .Produces<DonationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/donations/{donationId:guid}/collect", async (
                Guid donationId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new MarkDonationCollectedCommand(donationId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CollectTrustDonation")
            .WithSummary("Mark a pledge collected, issuing the Trust's next receipt number.")
            .Produces<DonationResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // ---- Expenses --------------------------------------------------------

        group.MapGet("/trusts/{id:guid}/expenses", async (
                Guid id,
                ExpenseStatus? status,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListExpensesQuery(id, status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListTrustExpenses")
            .WithSummary("A Trust's expenses, newest first, optionally only Recorded or Approved.")
            .Produces<IReadOnlyList<ExpenseResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/trusts/{id:guid}/expenses", async (
                Guid id,
                RecordExpenseRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new RecordExpenseCommand(
                    id, request.Category, request.Description, request.Amount, request.Vendor);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/trust/expenses/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("RecordTrustExpense")
            .WithSummary("Record an expense. It stays Recorded until somebody approves it.")
            .Produces<ExpenseResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/expenses/{expenseId:guid}/approve", async (
                Guid expenseId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ApproveExpenseCommand(expenseId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ApproveTrustExpense")
            .WithSummary("Approve an expense. Approving one already approved changes nothing.")
            .Produces<ExpenseResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // ---- Collections -----------------------------------------------------

        group.MapGet("/trusts/{id:guid}/collections", async (
                Guid id,
                CollectionStatus? status,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListCollectionsQuery(id, status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListTrustCollections")
            .WithSummary("Amounts due to a Trust, newest first, optionally only Due or Collected.")
            .Produces<IReadOnlyList<CollectionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/collections/{collectionId:guid}/payment", async (
                Guid collectionId,
                RecordPaymentRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RecordCollectionPaymentCommand(collectionId, request.Mode), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RecordTrustCollectionPayment")
            .WithSummary("Record that an amount due was paid, and how.")
            .Produces<CollectionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/collections/mine", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListMyDuesQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListMyTrustDues")
            .WithSummary("What the caller still owes to a Trust in their Samaaj, oldest first.")
            .Produces<IReadOnlyList<CollectionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary><c>DonorMemberId</c> is a string, not a Guid - see RecordDonationCommand.</summary>
    public sealed record RecordDonationRequest(
        string? DonorMemberId,
        string? DonorName,
        string? DonorPhone,
        long Amount,
        string Purpose,
        string Mode);

    public sealed record RecordExpenseRequest(string Category, string Description, long Amount, string? Vendor);

    public sealed record RecordPaymentRequest(string Mode);
}
