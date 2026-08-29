namespace Sangam.Events.Application.Common;

/// <summary>
/// Why a <see cref="Result"/> failed. The type drives the HTTP status code at
/// the API boundary, so handlers never reason about status codes themselves.
/// </summary>
public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
}

public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Field-level failures, populated only for validation errors.</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; init; } =
        new Dictionary<string, string[]>();

    /// <summary>
    /// Compared structurally, including the field errors. The compiler-generated
    /// record equality would compare the dictionary by reference, so two
    /// identical "invalid credentials" errors would come out unequal - which
    /// silently breaks any code, or test, that asserts two paths fail the same
    /// way.
    /// </summary>
    public bool Equals(Error? other) =>
        other is not null
        && Code == other.Code
        && Description == other.Description
        && Type == other.Type
        && FieldErrorsEqual(FieldErrors, other.FieldErrors);

    public override int GetHashCode() => HashCode.Combine(Code, Description, Type, FieldErrors.Count);

    private static bool FieldErrorsEqual(
        IReadOnlyDictionary<string, string[]> left,
        IReadOnlyDictionary<string, string[]> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        return left.All(entry =>
            right.TryGetValue(entry.Key, out var messages) && entry.Value.SequenceEqual(messages));
    }

    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);

    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);

    public static Error Validation(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new("Validation", "One or more validation errors occurred.", ErrorType.Validation)
        {
            FieldErrors = fieldErrors,
        };
}
