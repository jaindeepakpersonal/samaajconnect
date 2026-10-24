using MediatR;
using Sangam.Businesses.Api.Extensions;
using Sangam.Businesses.Application.Businesses;
using Sangam.Businesses.Application.Businesses.Commands.Photos;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Domain.Media;

namespace Sangam.Businesses.Api.Endpoints;

/// <summary>
/// A business's photo and its products' images: upload, serve, remove.
/// </summary>
/// <remarks>
/// Its own file rather than joining <c>BusinessEndpoints</c>, per CLAUDE.md
/// section 4.6. The bounded multipart read is events-service's event photo
/// copied, because services share no code by design.
/// </remarks>
public static class BusinessPhotoEndpoints
{
    /// <summary>
    /// A little over the domain's cap, so a file just above the limit is read
    /// far enough to be refused for its size rather than being cut off and
    /// refused for not looking like an image.
    /// </summary>
    private const long MaxUploadBytes = ImageContent.MaxBytes + (64 * 1024);

    public static IEndpointRouteBuilder MapBusinessPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/businesses").WithTags("Businesses").RequireAuthorization();

        group.MapPost("/mine/{businessId:guid}/photo", async (
                Guid businessId, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var (bytes, error) = await ReadAsync(request, cancellationToken);

                if (error is not null)
                {
                    return error;
                }

                return (await sender.Send(new UploadBusinessPhotoCommand(businessId, bytes!), cancellationToken))
                    .ToApiResult();
            })
            .WithName("UploadBusinessPhoto")
            .WithSummary("Upload a business's photo. JPEG, PNG or WebP, 2 MB or smaller. The owner only; answers the whole business.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<BusinessResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .DisableAntiforgery();

        group.MapDelete("/mine/{businessId:guid}/photo", async (
                Guid businessId, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new RemoveBusinessPhotoCommand(businessId), cancellationToken)).ToApiResult())
            .WithName("RemoveBusinessPhoto")
            .WithSummary("Take a business's photo down. Doing it twice is success. The owner only.")
            .Produces<BusinessResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/mine/{businessId:guid}/products/{productId:guid}/photo", async (
                Guid businessId, Guid productId, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var (bytes, error) = await ReadAsync(request, cancellationToken);

                if (error is not null)
                {
                    return error;
                }

                return (await sender.Send(new UploadProductPhotoCommand(businessId, productId, bytes!), cancellationToken))
                    .ToApiResult();
            })
            .WithName("UploadProductPhoto")
            .WithSummary("Upload a product's image. JPEG, PNG or WebP, 2 MB or smaller. The owner only; answers the whole business.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<BusinessResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .DisableAntiforgery();

        group.MapDelete("/mine/{businessId:guid}/products/{productId:guid}/photo", async (
                Guid businessId, Guid productId, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new RemoveProductPhotoCommand(businessId, productId), cancellationToken)).ToApiResult())
            .WithName("RemoveProductPhoto")
            .WithSummary("Take a product's image down. Doing it twice is success. The owner only.")
            .Produces<BusinessResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{businessId:guid}/photo", async (
                Guid businessId, HttpContext context, ISender sender, CancellationToken cancellationToken) =>
                Serve(await sender.Send(new GetBusinessImageQuery(businessId, null), cancellationToken), context))
            .WithName("GetBusinessPhoto")
            .WithSummary("A business's photo, for whoever may open the business.")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{businessId:guid}/products/{productId:guid}/photo", async (
                Guid businessId, Guid productId, HttpContext context, ISender sender, CancellationToken cancellationToken) =>
                Serve(await sender.Send(new GetBusinessImageQuery(businessId, productId), cancellationToken), context))
            .WithName("GetProductPhoto")
            .WithSummary("A product's image, for whoever may see the product.")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
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
    /// <c>Cache-Control: private</c> - served per signed-in request, so no
    /// shared cache may hold it.
    /// </summary>
    private static IResult Serve(Result<BusinessImageContent> result, HttpContext context)
    {
        if (!result.IsSuccess)
        {
            return result.ToApiResult();
        }

        var image = result.Value!;
        var etag = $"\"{image.ETag}\"";

        context.Response.Headers.CacheControl = "private, max-age=300";
        context.Response.Headers.ETag = etag;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        if (context.Request.Headers.IfNoneMatch is { Count: > 0 } known && known.Contains(etag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(image.Bytes, image.ContentType, lastModified: image.UploadedAt);
    }
}
