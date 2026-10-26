using System.Diagnostics.CodeAnalysis;

namespace OnePondy.Application.Common;

/// <summary>How an error should be reported to clients; the API maps each kind to an HTTP status.</summary>
public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    TooManyRequests,
    Unavailable,
}

/// <param name="Code">Stable, machine-readable code that clients translate, e.g. <c>invalid_code</c>.</param>
/// <param name="Detail">English description for logs and developers.</param>
/// <param name="FieldErrors">Problems per input field, keyed by the field's JSON name, for forms.</param>
public sealed record AppError(
    string Code,
    ErrorKind Kind,
    string Detail,
    TimeSpan? RetryAfter = null,
    IReadOnlyList<string>? Reasons = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public static AppError Validation(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new("validation_failed", ErrorKind.Validation, "Some fields are not valid.", FieldErrors: fieldErrors);
}

public sealed class Result<T>
{
    private Result(T? value, AppError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public AppError? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(AppError error) => new(default, error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(AppError error) => Failure(error);
}
