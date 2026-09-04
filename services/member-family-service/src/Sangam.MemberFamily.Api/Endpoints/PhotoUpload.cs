using Sangam.MemberFamily.Api.Extensions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Media;
using Sangam.MemberFamily.Domain.Media;

namespace Sangam.MemberFamily.Api.Endpoints;

/// <summary>
/// Reading a bounded multipart upload, and writing image bytes back.
/// </summary>
/// <remarks>
/// Shared by the member and child photo endpoints, which are separate files per
/// CLAUDE.md §4.6 but have exactly the same transport problem. Both of those
/// stay thin mapping; this is the one place that touches an HTTP body directly,
/// and the reason it is allowed to is that none of it is business logic - it is
/// what "bind the request" means when the request is a file.
/// </remarks>
public static class PhotoUpload
{
    /// <summary>
    /// A little over the domain's cap, so a file just above the limit is read
    /// far enough to be refused for its size rather than being cut off and
    /// refused for not looking like an image.
    /// </summary>
    private const long MaxUploadBytes = ImageContent.MaxBytes + (64 * 1024);

    /// <summary>
    /// The uploaded bytes, or the response to send instead.
    /// </summary>
    /// <remarks>
    /// Everything here is about not trusting the request. The declared length is
    /// checked first because it is free; the copy is then bounded anyway,
    /// because <c>Content-Length</c> is a number the client chose and a chunked
    /// upload has none at all. Only the second check is load-bearing — the first
    /// exists so the common honest case fails fast.
    /// </remarks>
    internal static async Task<(byte[]? Bytes, IResult? Error)> ReadAsync(
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

        // Bounded regardless of what the headers claimed.
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

    internal static IResult TooLarge() => Results.Problem(
        title: $"A photo has to be {ImageContent.MaxBytes / (1024 * 1024)} MB or smaller.",
        detail: "Resize the image and try again.",
        statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>
    /// Writes image bytes, or maps the failure the way every other endpoint
    /// does.
    /// </summary>
    /// <remarks>
    /// <c>Cache-Control: private</c> is the important header and the reason this
    /// is not <c>public</c>: these bytes were served because <i>this</i> caller
    /// was allowed to see them, so a shared cache holding them would hand them
    /// to the next caller without the check. <c>max-age</c> is short for the
    /// same reason a removed photo should stop appearing quickly; the ETag is
    /// what actually saves the bytes on a repeat visit.
    /// </remarks>
    internal static IResult Serve(Result<PhotoContent> result, HttpContext context)
    {
        if (!result.IsSuccess)
        {
            return result.ToApiResult();
        }

        var photo = result.Value!;
        var etag = $"\"{photo.ETag}\"";

        context.Response.Headers.CacheControl = "private, max-age=300";
        context.Response.Headers.ETag = etag;

        // Nothing here is ever a document a browser should interpret - the
        // stored type is one of three raster formats, sniffed from the bytes -
        // but saying so costs a header and removes the question.
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        var known = context.Request.Headers.IfNoneMatch;

        if (known.Count > 0 && known.Contains(etag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(photo.Bytes, photo.ContentType, lastModified: photo.UploadedAt);
    }
}
