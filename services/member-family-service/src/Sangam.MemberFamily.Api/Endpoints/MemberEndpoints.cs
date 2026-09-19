using MediatR;
using Sangam.MemberFamily.Api.Extensions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Members;
using Sangam.MemberFamily.Application.Members.Commands.CorrectMemberDetails;
using Sangam.MemberFamily.Application.Members.Commands.UpdateProfile;
using Sangam.MemberFamily.Application.Members.Queries.GetMember;
using Sangam.MemberFamily.Application.Members.Queries.GetMyData;
using Sangam.MemberFamily.Application.Members.Queries.GetMyProfile;
using Sangam.MemberFamily.Application.Members.Queries.GetRegistrationSummary;
using Sangam.MemberFamily.Application.Members.Queries.SearchMembers;

namespace Sangam.MemberFamily.Api.Endpoints;

public static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/members").WithTags("Members").RequireAuthorization();

        group.MapGet("/", async (
                string? term,
                Guid? localityId,
                int? skip,
                int? take,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new SearchMembersQuery(term, localityId, skip ?? 0, take ?? 20), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("SearchMembers")
            .WithSummary("Search this Samaaj's member directory. Fields respect each member's privacy settings.")
            .Produces<PagedResult<MemberResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/me", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMyProfileQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("GetMyProfile")
            .WithSummary("The caller's own profile, complete regardless of privacy settings.")
            .Produces<MyProfileResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/me/data-export", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMyDataQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("ExportMyMemberData")
            .WithSummary("Everything this service holds about you and your household (DPDP s.11).")
            .Produces<MyMemberDataResponse>();

        group.MapGet("/registration-summary", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetRegistrationSummaryQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("GetRegistrationSummary")
            .WithSummary("Registration counts for the current 30-day window against the prior one.")
            .Produces<RegistrationSummaryResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Declared after /me, /me/data-export and /registration-summary so
        // those literal routes are never shadowed by the id parameter.
        group.MapGet("/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMemberQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("GetMember")
            .WithSummary(
                "One member of this Samaaj, through the same per-field privacy mapper the "
                + "directory uses.")
            .Produces<MemberResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", async (
                Guid id,
                UpdateProfileRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new UpdateProfileCommand(
                    id,
                    request.FullName,
                    request.DateOfBirth,
                    request.Gender,
                    request.Mobile,
                    request.Email,
                    request.GautraId,
                    request.ProfessionId,
                    request.QualificationId,
                    request.PermanentAddress,
                    request.TemporaryAddress,
                    request.Privacy,
                    request.IsListedInDirectory);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .WithName("UpdateProfile")
            .WithSummary("Update your own profile, including what your Samaaj may see.")
            .Produces<MyProfileResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // A Samaaj administrator correcting somebody else's details.
        //
        // A separate path rather than a flag on the one above, because the two
        // requests carry different things: this one has no privacy fields, and
        // that absence is the guarantee. A shared endpoint that ignored them
        // for one caller and honoured them for another would put the rule in a
        // handler's `if` instead of in the shape of the request.
        group.MapPatch("/{id:guid}/details", async (
                Guid id,
                CorrectMemberDetailsRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new CorrectMemberDetailsCommand(
                    id,
                    request.FullName,
                    request.DateOfBirth,
                    request.Gender,
                    request.Mobile,
                    request.Email,
                    request.GautraId,
                    request.ProfessionId,
                    request.QualificationId,
                    request.PermanentAddress,
                    request.TemporaryAddress);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .WithName("CorrectMemberDetails")
            .WithSummary("Correct another member's details. Never what they share.")
            .Produces<MemberResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record UpdateProfileRequest(
        string FullName,
        DateOnly? DateOfBirth,
        string? Gender,
        string? Mobile,
        string? Email,
        Guid? GautraId,
        Guid? ProfessionId,
        Guid? QualificationId,
        ProfileAddressInput PermanentAddress,
        ProfileAddressInput? TemporaryAddress,
        PrivacySettings Privacy,
        bool? IsListedInDirectory);

    /// <summary>
    /// The same factual fields, and deliberately no privacy ones.
    /// </summary>
    public sealed record CorrectMemberDetailsRequest(
        string FullName,
        DateOnly? DateOfBirth,
        string? Gender,
        string? Mobile,
        string? Email,
        Guid? GautraId,
        Guid? ProfessionId,
        Guid? QualificationId,
        ProfileAddressInput PermanentAddress,
        ProfileAddressInput? TemporaryAddress);
}
