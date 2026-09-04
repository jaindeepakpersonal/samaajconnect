using MediatR;
using Sangam.MemberFamily.Api.Extensions;
using Sangam.MemberFamily.Application.Media;

namespace Sangam.MemberFamily.Api.Endpoints;

/// <summary>
/// A child's photo: upload, serve, remove.
/// </summary>
/// <remarks>
/// Its own file rather than sharing one with the member photo endpoints, and
/// that is CLAUDE.md §4.6 rather than taste — one file per aggregate. The first
/// version put both groups in one file and `scripts/unreachable-endpoints.sh`
/// caught it: the sweep reads the first `MapGroup` in a file as the prefix for
/// everything in it, so three `/v1/children` routes were reported as
/// `/v1/members` ones and quietly deduplicated against their namesakes. A
/// convention with a tool that assumes it is a convention worth keeping.
///
/// The upload helpers live on <see cref="PhotoUpload"/>, shared with the member
/// endpoints, because reading a bounded multipart body is the same problem in
/// both places.
/// </remarks>
public static class ChildPhotoEndpoints
{
    public static IEndpointRouteBuilder MapChildPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var children = app.MapGroup("/v1/children").WithTags("Children").RequireAuthorization();

        children.MapPost("/{id:guid}/photo", async (
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
                    new UploadChildPhotoCommand(id, bytes!), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("UploadChildPhoto")
            .WithSummary("Upload a child's photo. Their household only.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ChildPhotoResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .DisableAntiforgery();

        children.MapGet("/{id:guid}/photo", async (
                Guid id,
                HttpContext context,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetChildPhotoQuery(id), cancellationToken);

                return PhotoUpload.Serve(result, context);
            })
            .WithName("GetChildPhoto")
            .WithSummary("A child's photo, to their own household.")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .ProducesProblem(StatusCodes.Status404NotFound);

        children.MapDelete("/{id:guid}/photo", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RemoveChildPhotoCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("RemoveChildPhoto")
            .WithSummary("Take a child's photo down. Doing it twice is success.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
