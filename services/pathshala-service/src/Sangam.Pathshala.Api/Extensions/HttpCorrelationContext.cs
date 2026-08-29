using Sangam.Pathshala.Application.Abstractions;

namespace Sangam.Pathshala.Api.Extensions;

public sealed class HttpCorrelationContext : ICorrelationContext
{
    public const string CorrelationHeader = "X-Correlation-Id";

    public HttpCorrelationContext(IHttpContextAccessor accessor)
    {
        var httpContext = accessor.HttpContext;

        CorrelationId = httpContext is null
            ? Guid.NewGuid().ToString("n")
            : ReadOrCreate(httpContext);
    }

    public string CorrelationId { get; }

    private static string ReadOrCreate(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue(CorrelationHeader, out var raw)
            && !string.IsNullOrWhiteSpace(raw))
        {
            return raw.ToString();
        }

        // Falling back to TraceIdentifier rather than a fresh Guid keeps the id
        // aligned with whatever ASP.NET already logged for this request.
        return httpContext.TraceIdentifier;
    }
}
