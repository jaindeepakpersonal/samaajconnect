using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;

namespace Sangam.CelebrityVoting.Application.Behaviors;

/// <summary>
/// Behavior 4 of 5 (CLAUDE.md §4.4). Commands only — queries don't mutate, so
/// they never pay for a transaction. Because it sits after ValidationBehavior,
/// an invalid request never holds a transaction open.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private static readonly ConcurrentDictionary<Type, bool> IsCommandCache = new();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var isCommand = IsCommandCache.GetOrAdd(typeof(TRequest), static type =>
            type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)));

        if (!isCommand || unitOfWork.HasActiveTransaction)
        {
            return await next();
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        TResponse response;

        try
        {
            response = await next();
        }
        catch
        {
            // Let UnhandledExceptionBehavior classify it; this scope only owes
            // the connection a rollback.
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        if (response.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            logger.LogDebug(
                "Rolling back {Request}: handler returned {ErrorCode}",
                typeof(TRequest).Name,
                response.Error.Code);

            await transaction.RollbackAsync(cancellationToken);
        }

        return response;
    }
}
