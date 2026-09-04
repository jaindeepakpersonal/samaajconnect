using MediatR;
using Sangam.MemberFamily.Api.Extensions;
using Sangam.MemberFamily.Application.Families;
using Sangam.MemberFamily.Application.Families.Commands.CreateFamily;
using Sangam.MemberFamily.Application.Families.Commands.DecideJoinRequest;
using Sangam.MemberFamily.Application.Families.Commands.RequestJoinFamily;
using Sangam.MemberFamily.Application.Families.Commands.WithdrawJoinRequest;
using Sangam.MemberFamily.Application.Families.Queries.GetMyFamily;

namespace Sangam.MemberFamily.Api.Endpoints;

public static class FamilyEndpoints
{
    public static IEndpointRouteBuilder MapFamilyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/families").WithTags("Families").RequireAuthorization();

        group.MapPost("/", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateFamilyCommand(), cancellationToken);

                return result.ToApiResult(family =>
                    Results.Created($"/v1/families/{family.Id}", family));
            })
            .WithName("CreateFamily")
            .WithSummary("Create a household with you as its head.")
            .Produces<FamilyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/mine", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMyFamilyQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("GetMyFamily")
            .WithSummary("Your household. The family code is returned only to its head.")
            .Produces<FamilyResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/join-requests", async (
                JoinRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RequestJoinFamilyCommand(request.FamilyCode, request.Relationship), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("RequestJoinFamily")
            .WithSummary("Ask to join a household using the code its head shared.")
            .Produces<FamilyResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // DELETE on the collection, because a member has at most one standing
        // request and it is their own. An id in the path would be a fact the
        // caller cannot get wrong and the server already knows.
        group.MapDelete("/join-requests/mine", async (
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new WithdrawJoinRequestCommand(), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("WithdrawJoinRequest")
            .WithSummary("Take back a request to join a household that nobody has decided yet.")
            .Produces<WithdrawJoinRequestResult>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{familyId:guid}/join-requests/{requestId:guid}/decide", async (
                Guid familyId,
                Guid requestId,
                DecideRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new DecideJoinRequestCommand(familyId, requestId, request.Accept), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("DecideJoinRequest")
            .WithSummary("Accept or reject a join request. Head of that family only.")
            .Produces<FamilyResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record JoinRequest(string FamilyCode, string Relationship);

    public sealed record DecideRequest(bool Accept);
}
