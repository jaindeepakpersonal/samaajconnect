using MediatR;
using Sangam.MemberFamily.Api.Extensions;
using Sangam.MemberFamily.Application.Members;
using Sangam.MemberFamily.Application.Members.Commands.UpdateProfile;
using Sangam.MemberFamily.Application.Members.Queries.GetMember;
using Sangam.MemberFamily.Application.Members.Queries.GetMyData;
using Sangam.MemberFamily.Application.Members.Queries.GetMyProfile;
using Sangam.MemberFamily.Application.Members.Queries.SearchMembers;

namespace Sangam.MemberFamily.Api.Endpoints;

public static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/members").WithTags("Members").RequireAuthorization();

        group.MapGet("/", async (
                string? term,
                string? locality,
                int? limit,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new SearchMembersQuery(term, locality, limit ?? 50), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("SearchMembers")
            .WithSummary("Search this Samaaj's member directory. Fields respect each member's privacy settings.")
            .Produces<IReadOnlyList<MemberResponse>>()
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

        // Declared after /me and /me/data-export so those literal routes are
        // never shadowed by the id parameter.
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
                    request.PhotoUrl,
                    request.DateOfBirth,
                    request.Gender,
                    request.Mobile,
                    request.Email,
                    request.Address,
                    request.Locality,
                    request.Profession,
                    request.Privacy);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .WithName("UpdateProfile")
            .WithSummary("Update a profile. Your own, or anyone's in this Samaaj with Members.Write.")
            .Produces<MyProfileResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record UpdateProfileRequest(
        string FullName,
        string? PhotoUrl,
        DateOnly? DateOfBirth,
        string? Gender,
        string? Mobile,
        string? Email,
        string? Address,
        string? Locality,
        string? Profession,
        PrivacySettings Privacy);
}
