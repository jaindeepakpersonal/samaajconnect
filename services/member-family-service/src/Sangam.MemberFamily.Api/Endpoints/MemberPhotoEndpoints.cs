using MediatR;
using Sangam.MemberFamily.Api.Extensions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Media;
using Sangam.MemberFamily.Domain.Media;

namespace Sangam.MemberFamily.Api.Endpoints;

/// <summary>
/// Uploading and serving the photos this platform hosts.
/// </summary>
/// <remarks>
/// <para>
/// Thin mapping, like every other endpoint file here (CLAUDE.md §4.6) — with
/// one thing these do that a JSON endpoint does not, and it is worth naming:
/// <b>reading the multipart body is capped before the bytes are read, not
/// after.</b> A handler that reads an arbitrarily large upload into memory and
/// then rejects it has already done the expensive thing the limit exists to
/// prevent, so the length is checked first and the copy is bounded.
/// </para>
/// <para>
/// The response for a photo is written directly rather than through
/// <c>ToApiResult()</c>, because the body is bytes rather than JSON. The
/// failure paths still go through it, so a 404 here looks like a 404 anywhere
/// else on the platform.
/// </para>
/// </remarks>
public static class MemberPhotoEndpoints
{
    public static IEndpointRouteBuilder MapMemberPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup("/v1/members").WithTags("Members").RequireAuthorization();

        members.MapPost("/{id:guid}/photo", async (
                Guid id,
                HttpRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var (bytes, error) = await PhotoUpload.ReadAsync(request, cancellationToken);

                if (error is not null)
                {
                    return error;
                }

                var result = await sender.Send(
                    new UploadMemberPhotoCommand(id, bytes!), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("UploadMemberPhoto")
            .WithSummary("Upload a member's photo. JPEG, PNG or WebP, 2 MB or smaller.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<MemberPhotoResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .DisableAntiforgery();

        members.MapGet("/{id:guid}/photo", async (
                Guid id,
                HttpContext context,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMemberPhotoQuery(id), cancellationToken);

                return PhotoUpload.Serve(result, context);
            })
            .WithName("GetMemberPhoto")
            .WithSummary("A member's photo, if they have one and you may see them.")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        members.MapDelete("/{id:guid}/photo", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RemoveMemberPhotoCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("RemoveMemberPhoto")
            .WithSummary("Take a member's photo down. Doing it twice is success.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

}
