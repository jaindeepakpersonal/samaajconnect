using MediatR;
using Sangam.Matrimony.Api.Extensions;
using Sangam.Matrimony.Application.Proposals;
using Sangam.Matrimony.Application.Proposals.Commands.RespondToProposal;
using Sangam.Matrimony.Application.Proposals.Commands.SendProposal;
using Sangam.Matrimony.Application.Proposals.Commands.WithdrawProposal;
using Sangam.Matrimony.Application.Proposals.Queries.ListReceivedProposals;
using Sangam.Matrimony.Application.Proposals.Queries.ListSentProposals;

namespace Sangam.Matrimony.Api.Endpoints;

public static class ProposalEndpoints
{
    public static IEndpointRouteBuilder MapProposalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/matrimony/proposals").WithTags("Matrimony proposals");

        group.MapPost("/", async (
                SendProposalRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new SendProposalCommand(request.ToMemberId, request.Message), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("SendProposal")
            .WithSummary("Send a proposal to a candidate found via search.")
            .Produces<ProposalResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/sent", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListSentProposalsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListSentProposals")
            .WithSummary("Proposals the caller has sent.")
            .Produces<IReadOnlyList<ProposalResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/received", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListReceivedProposalsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListReceivedProposals")
            .WithSummary("Proposals the caller has received.")
            .Produces<IReadOnlyList<ProposalResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/respond", async (
                Guid id, RespondRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RespondToProposalCommand(id, request.Accept), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RespondToProposal")
            .WithSummary("Accept or decline a proposal. The recipient only.")
            .Produces<ProposalResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/withdraw", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new WithdrawProposalCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("WithdrawProposal")
            .WithSummary("Take back a proposal nobody has decided yet. The sender only.")
            .Produces<WithdrawProposalResult>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record SendProposalRequest(Guid ToMemberId, string? Message);

    /// <summary>Required, no default - the same reasoning `DecideRequest` in volunteer-groups-service already states for a decision endpoint.</summary>
    public sealed record RespondRequest(bool Accept);
}
