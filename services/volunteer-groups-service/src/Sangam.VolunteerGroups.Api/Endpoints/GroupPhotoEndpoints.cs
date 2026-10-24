using MediatR;
using Sangam.VolunteerGroups.Api.Extensions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Groups.Commands.GroupPhoto;
using Sangam.VolunteerGroups.Domain.Media;

namespace Sangam.VolunteerGroups.Api.Endpoints;

/// <summary>
/// A volunteer group's photo: upload, serve, remove.
/// </summary>
/// <remarks>
/// Its own file rather than joining <c>GroupEndpoints</c>, per CLAUDE.md §4.6 -
/// and because <c>scripts/unreachable-endpoints.sh</c> reads the first
/// <c>MapGroup</c> in a file as the prefix for everything in it. The bounded
/// multipart read is the same shape as identity-tenant-service's logo upload:
/// copied, because services share no code by design.
/// </remarks>
public static class GroupPhotoEndpoints
{
    /// <summary>
    /// A little over the domain's cap, so a file just above the limit is read
    /// far enough to be refused for its size rather than being cut off and
    /// refused for not looking like an image.
    /// </summary>
    private const long MaxUploadBytes = ImageContent.MaxBytes + (64 * 1024);

    public static IEndpointRouteBuilder MapGroupPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/volunteer-groups").WithTags("Volunteer groups").RequireAuthorization();

        group.MapPost("/groups/{id:guid}/photo", async (
                Guid id,
                HttpRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var (bytes, error) = await ReadAsync(request, cancellationToken);

                if (error is not null)
                {
                    return error;
                }

                var result = await sender.Send(new UploadGroupPhotoCommand(id, bytes!), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("UploadGroupPhoto")
            .WithSummary("Upload a volunteer group's photo, its page banner. JPEG, PNG or WebP, 2 MB or smaller.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<GroupPhotoResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .DisableAntiforgery();

        group.MapGet("/groups/{id:guid}/photo", async (
                Guid id,
                HttpContext context,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetGroupPhotoQuery(id), cancellationToken);

                return Serve(result, context);
            })
            .WithName("GetGroupPhoto")
            .WithSummary("A volunteer group's photo, for signed-in members of the Samaaj.")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/groups/{id:guid}/photo", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RemoveGroupPhotoCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .WithName("RemoveGroupPhoto")
            .WithSummary("Take a volunteer group's photo down. Doing it twice is success.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>The uploaded bytes, or the response to send instead.</summary>
    /// <remarks>
    /// The declared length is checked first because it is free; the copy is
    /// bounded anyway, because <c>Content-Length</c> is a number the client
    /// chose and a chunked upload has none at all.
    /// </remarks>
    private static async Task<(byte[]? Bytes, IResult? Error)> ReadAsync(
        HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return (null, Results.Problem(
                title: "Upload a photo as a form file.",
                detail: "This endpoint takes multipart/form-data with a single file part.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.Count > 0 ? form.Files[0] : null;

        if (file is null || file.Length == 0)
        {
            return (null, Results.Problem(
                title: "No photo was uploaded.",
                detail: "Attach one image file.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        if (file.Length > MaxUploadBytes)
        {
            return (null, TooLarge());
        }

        using var buffer = new MemoryStream();
        await using var source = file.OpenReadStream();

        var chunk = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxUploadBytes)
            {
                return (null, TooLarge());
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), null);
    }

    private static IResult TooLarge() => Results.Problem(
        title: $"A photo has to be {ImageContent.MaxBytes / (1024 * 1024)} MB or smaller.",
        detail: "Resize the image and try again.",
        statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>
    /// Writes the bytes, or maps the failure the way every other endpoint does.
    /// <c>Cache-Control: private</c> - a group photo is often of people and is
    /// served per signed-in request, so no shared cache may hold it.
    /// </summary>
    private static IResult Serve(Result<GroupPhotoContent> result, HttpContext context)
    {
        if (!result.IsSuccess)
        {
            return result.ToApiResult();
        }

        var photo = result.Value!;
        var etag = $"\"{photo.ETag}\"";

        context.Response.Headers.CacheControl = "private, max-age=300";
        context.Response.Headers.ETag = etag;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        if (context.Request.Headers.IfNoneMatch is { Count: > 0 } known && known.Contains(etag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(photo.Bytes, photo.ContentType, lastModified: photo.UploadedAt);
    }
}
