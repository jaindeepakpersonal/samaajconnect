using MediatR;
using Sangam.MemberFamily.Api.Extensions;
using Sangam.MemberFamily.Application.Children;
using Sangam.MemberFamily.Application.Children.Commands.CreateChildProfile;
using Sangam.MemberFamily.Application.Children.Commands.DecideChildConversion;
using Sangam.MemberFamily.Application.Children.Commands.RequestChildConversion;
using Sangam.MemberFamily.Application.Children.Queries.GetChildDataNotice;
using Sangam.MemberFamily.Application.Children.Queries.ListConversionRequests;
using Sangam.MemberFamily.Application.Children.Queries.GetChildNames;
using Sangam.MemberFamily.Application.Children.Queries.ListFamilyChildren;

namespace Sangam.MemberFamily.Api.Endpoints;

public static class ChildEndpoints
{
    public static IEndpointRouteBuilder MapChildEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/children").WithTags("Children").RequireAuthorization();

        group.MapGet("/", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListFamilyChildrenQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("ListFamilyChildren")
            .WithSummary("Children in your household, with whether each is old enough to convert.")
            .Produces<IReadOnlyList<ChildResponse>>();

        group.MapGet("/names", async (
                string? ids, ISender sender, CancellationToken cancellationToken) =>
            {
                var parsed = (ids ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToArray();

                var result = await sender.Send(new GetChildNamesQuery(parsed), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("GetChildNames")
            .WithSummary(
                "Names for children an administrator already holds the ids of - the Pathshala "
                + "placement queue. Names only, deliberately.")
            .Produces<IReadOnlyList<ChildNameResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/data-notice", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetChildDataNoticeQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("GetChildDataNotice")
            .WithSummary("What a parent is shown before a child record is created (DPDP s.5 and s.9).")
            .Produces<ChildDataNoticeResponse>();

        group.MapPost("/", async (
                CreateChildRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateChildProfileCommand(
                    request.FullName,
                    request.DateOfBirth,
                    request.Gender,
                    request.ParentalConsentGiven,
                    request.NoticeVersion ?? string.Empty);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(child => Results.Created($"/v1/children/{child.Id}", child));
            })
            .WithName("CreateChildProfile")
            .WithSummary("Add a child to your household. Family head only.")
            .Produces<ChildResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/conversion", async (
                Guid id,
                ConversionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RequestChildConversionCommand(id, request.MobileOrEmail), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("RequestChildConversion")
            .WithSummary("Ask for a child who has turned 18 to be given their own member account.")
            .Produces<ConversionRequestResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/conversion-requests", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListConversionRequestsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("ListConversionRequests")
            .WithSummary("Conversion requests awaiting a decision. Samaaj admins only.")
            .Produces<IReadOnlyList<ConversionRequestResponse>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/conversion-requests/{requestId:guid}/decide", async (
                Guid requestId,
                DecideRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new DecideChildConversionCommand(requestId, request.Approve, request.Note),
                    cancellationToken);

                return result.ToApiResult();
            })
            .WithName("DecideChildConversion")
            .WithSummary("Approve or reject a conversion request. Samaaj admins only.")
            .Produces<ConversionRequestResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// <paramref name="ParentalConsentGiven"/> and <paramref name="NoticeVersion"/>
    /// are required: DPDP section 9 makes parental consent the basis on which a
    /// child`s data may be held, and section 6(7) means a consent that cannot
    /// say what was shown is worth little.
    /// </summary>
    public sealed record CreateChildRequest(
        string FullName,
        DateOnly DateOfBirth,
        string? Gender,
        bool ParentalConsentGiven,
        string? NoticeVersion);

    public sealed record ConversionRequest(string MobileOrEmail);

    public sealed record DecideRequest(bool Approve, string? Note);
}
