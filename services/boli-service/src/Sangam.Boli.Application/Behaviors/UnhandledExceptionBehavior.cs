using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Boli.Application.Common;

namespace Sangam.Boli.Application.Behaviors;

/// <summary>
/// Behavior 5 of 5 (CLAUDE.md §4.4). Converts genuinely unexpected failures
/// into a generic Result.Failure so no stack trace or SQL text ever reaches a
/// caller. Expected outcomes never get here — handlers return Result for those.
/// </summary>
public sealed class UnhandledExceptionBehavior<TRequest, TResponse>(
    ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unhandled exception for {Request}", typeof(TRequest).Name);

            return ResultFactory.Failure<TResponse>(
                Error.Failure("Unexpected", "An unexpected error occurred while processing the request."));
        }
    }
}
