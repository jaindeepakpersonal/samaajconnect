using MediatR;
using Sangam.IdentityTenant.Api.Extensions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Media;
using Sangam.IdentityTenant.Domain.Media;

namespace Sangam.IdentityTenant.Api.Endpoints;

/// <summary>
/// A Samaaj's logo: upload, serve, remove.
/// </summary>
/// <remarks>
/// Its own file rather than joining <c>TenantEndpoints</c>, per CLAUDE.md §4.6 —
/// and because <c>scripts/unreachable-endpoints.sh</c> reads the first
/// <c>MapGroup</c> in a file as the prefix for everything in it, so a second
/// group in one file is quietly mis-reported. That was learned the hard way in
/// member-family-service.
///
/// Reading a bounded multipart body is the same problem here as there, so the
/// helper is the same shape. It is copied rather than shared because the two
/// services share no code by design; it is small, and both copies are held
/// identical by <c>scripts/security-invariants.sh</c>.
/// </remarks>
public static class TenantLogoEndpoints
{
    /// <summary>
    /// A little over the domain's cap, so a file just above the limit is read
    /// far enough to be refused for its size rather than being cut off and
    /// refused for not looking like an image.
    /// </summary>
    private const long MaxUploadBytes = ImageContent.MaxBytes + (64 * 1024);

    public static IEndpointRouteBuilder MapTenantLogoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/identity/tenants").WithTags("Tenants");

        group.MapPost("/{id:guid}/logo", async (
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

                var result = await sender.Send(
                    new UploadTenantLogoCommand(id, bytes!), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UploadTenantLogo")
            .WithSummary("Upload a Samaaj's logo. JPEG, PNG or WebP, 2 MB or smaller.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<TenantLogoResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .DisableAntiforgery();

        // Anonymous, deliberately: the registration form asks somebody to pick
        // their Samaaj before they have an account, and the directory it draws
        // from is anonymous for the same reason. GetTenantLogoQuery's remarks
        // carry the full reasoning, and SECURITY-CHECKLIST.md records that this
        // is the one image on the platform outside per-request authorization.
        group.MapGet("/{id:guid}/logo", async (
                Guid id,
                HttpContext context,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetTenantLogoQuery(id), cancellationToken);

                return Serve(result, context);
            })
            .AllowAnonymous()
            .WithName("GetTenantLogo")
            .WithSummary("A Samaaj's logo. Public, like the Samaaj directory it appears in.")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}/logo", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RemoveTenantLogoCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RemoveTenantLogo")
            .WithSummary("Take a Samaaj's logo down. Doing it twice is success.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// The uploaded bytes, or the response to send instead.
    /// </summary>
    /// <remarks>
    /// Everything here is about not trusting the request. The declared length is
    /// checked first because it is free; the copy is bounded anyway, because
    /// <c>Content-Length</c> is a number the client chose and a chunked upload
    /// has none at all. Only the second check is load-bearing — the first exists
    /// so the common honest case fails fast.
    /// </remarks>
    private static async Task<(byte[]? Bytes, IResult? Error)> ReadAsync(
        HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return (null, Results.Problem(
                title: "Upload a logo as a form file.",
                detail: "This endpoint takes multipart/form-data with a single file part.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.Count > 0 ? form.Files[0] : null;

        if (file is null || file.Length == 0)
        {
            return (null, Results.Problem(
                title: "No logo was uploaded.",
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
        title: $"A logo has to be {ImageContent.MaxBytes / (1024 * 1024)} MB or smaller.",
        detail: "Resize the image and try again.",
        statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>
    /// Writes logo bytes, or maps the failure the way every other endpoint does.
    /// </summary>
    /// <remarks>
    /// <c>Cache-Control: public</c>, which is the opposite of what a member's
    /// photo gets and for the same reason the endpoint is anonymous: this is an
    /// organisation's mark rather than a person, so a shared cache holding it
    /// hands it to callers who were always entitled to it. That makes serving
    /// logos cheaper than serving photos rather than more expensive.
    /// </remarks>
    private static IResult Serve(Result<LogoContent> result, HttpContext context)
    {
        if (!result.IsSuccess)
        {
            return result.ToApiResult();
        }

        var logo = result.Value!;
        var etag = $"\"{logo.ETag}\"";

        context.Response.Headers.CacheControl = "public, max-age=3600";
        context.Response.Headers.ETag = etag;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        var known = context.Request.Headers.IfNoneMatch;

        if (known.Count > 0 && known.Contains(etag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(logo.Bytes, logo.ContentType, lastModified: logo.UploadedAt);
    }
}
