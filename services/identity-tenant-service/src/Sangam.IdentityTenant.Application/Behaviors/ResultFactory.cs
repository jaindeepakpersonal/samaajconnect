using System.Collections.Concurrent;
using System.Reflection;
using Sangam.IdentityTenant.Application.Common;

namespace Sangam.IdentityTenant.Application.Behaviors;

/// <summary>
/// Builds a failed <c>Result&lt;T&gt;</c> when only the closed response type is
/// known at runtime. Pipeline behaviors are generic over TResponse and need to
/// short-circuit with a failure without knowing T at compile time.
/// </summary>
internal static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>> Factories = new();

    private static readonly MethodInfo GenericFailure = typeof(Result)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m is { Name: nameof(Result.Failure), IsGenericMethodDefinition: true });

    public static TResponse Failure<TResponse>(Error error)
    {
        var responseType = typeof(TResponse);

        if (responseType == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        var factory = Factories.GetOrAdd(responseType, static type =>
        {
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Result<>))
            {
                throw new InvalidOperationException(
                    $"Pipeline behaviors require a Result or Result<T> response, but got '{type}'.");
            }

            var closed = GenericFailure.MakeGenericMethod(type.GetGenericArguments()[0]);
            return error => closed.Invoke(null, [error])!;
        });

        return (TResponse)factory(error);
    }
}
