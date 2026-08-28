using Sangam.MemberFamily.Application.Common;

namespace Sangam.MemberFamily.Api.Extensions;

/// <summary>
/// The single place where a domain outcome becomes an HTTP status code. Keeping
/// it here is what lets handlers stay transport-agnostic (CLAUDE.md section 4.6).
/// </summary>
public static class ResultExtensions
{
    public static IResult ToApiResult<TValue>(this Result<TValue> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : Problem(result.Error);

    public static IResult ToApiResult<TValue>(this Result<TValue> result, Func<TValue, IResult> onSuccess) =>
        result.IsSuccess
            ? onSuccess(result.Value)
            : Problem(result.Error);

    public static IResult ToApiResult(this Result result) =>
        result.IsSuccess
            ? Results.NoContent()
            : Problem(result.Error);

    private static IResult Problem(Error error)
    {
        if (error.Type == ErrorType.Validation)
        {
            return Results.ValidationProblem(
                error.FieldErrors.ToDictionary(kv => kv.Key, kv => kv.Value),
                title: error.Description);
        }

        var statusCode = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: statusCode);
    }
}
