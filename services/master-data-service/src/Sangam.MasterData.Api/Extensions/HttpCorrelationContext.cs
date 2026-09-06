using Sangam.MasterData.Application.Abstractions;

namespace Sangam.MasterData.Api.Extensions;

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

        return httpContext.TraceIdentifier;
    }
}
