using FluentValidation;
using MediatR;
using Sangam.Timeline.Application.Common;

namespace Sangam.Timeline.Application.Behaviors;

/// <summary>
/// Behavior 3 of 5 (CLAUDE.md §4.4). Collects every failure from every
/// validator into one Result.Failure — validation never throws, and the caller
/// gets all their mistakes at once rather than one per round trip.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var applicable = validators as IValidator<TRequest>[] ?? validators.ToArray();

        if (applicable.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        // Sequential, not Task.WhenAll: a validator is free to inject a scoped
        // DbContext, and running two of those concurrently on one scope throws.
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var validator in applicable)
        {
            var validationResult = await validator.ValidateAsync(context, cancellationToken);

            failures.AddRange(validationResult.Errors.Where(f => f is not null));
        }

        if (failures.Count == 0)
        {
            return await next();
        }

        var fieldErrors = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => f.ErrorMessage).Distinct().ToArray());

        return ResultFactory.Failure<TResponse>(Error.Validation(fieldErrors));
    }
}
