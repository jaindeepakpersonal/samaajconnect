namespace Sangam.IdentityTenant.Application.Common;

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
