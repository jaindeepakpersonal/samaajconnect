using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;

namespace Sangam.CelebrityVoting.Application.Behaviors;

/// <summary>Behavior 1 of 5 (CLAUDE.md §4.4). Structured log + correlation id.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICorrelationContext correlationContext,
    ITenantContext tenantContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationContext.CorrelationId,
            ["TenantId"] = tenantContext.TenantId,
            ["Request"] = requestName,
        });

        logger.LogInformation("Handling {Request}", requestName);

        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        if (response.IsSuccess)
        {
            logger.LogInformation(
                "Handled {Request} in {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);
        }
        else
        {
            // Failures are expected outcomes, not incidents — log at Warning so
            // "issue already published" doesn't page anyone.
            logger.LogWarning(
                "{Request} failed in {ElapsedMs}ms with {ErrorType}/{ErrorCode}: {ErrorDescription}",
                requestName,
                stopwatch.ElapsedMilliseconds,
                response.Error.Type,
                response.Error.Code,
                response.Error.Description);
        }

        return response;
    }
}
