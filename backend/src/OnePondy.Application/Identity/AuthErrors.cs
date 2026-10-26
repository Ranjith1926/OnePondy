using OnePondy.Application.Common;

namespace OnePondy.Application.Identity;

public static class AuthErrors
{
    public static readonly AppError InvalidDestination =
        new("invalid_destination", ErrorKind.Validation, "Enter a valid mobile number or email address.");

    public static readonly AppError UnsupportedCountry = new(
        "unsupported_country",
        ErrorKind.Validation,
        "SMS codes are available for Indian mobile numbers. Use email instead.");

    public static readonly AppError DailyLimitReached = new(
        "codes_unavailable",
        ErrorKind.Unavailable,
        "Sign-in codes are temporarily unavailable. Try again later.");

    public static readonly AppError DeliveryUnavailable = new(
        "delivery_unavailable",
        ErrorKind.Unavailable,
        "Codes cannot be sent right now.");

    public static readonly AppError InvalidCode = new("invalid_code", ErrorKind.Validation, "The code is not correct.");

    public static readonly AppError CodeExpired =
        new("code_expired", ErrorKind.Validation, "The code has expired. Request a new one.");

    public static readonly AppError TooManyAttempts = new(
        "too_many_attempts",
        ErrorKind.Validation,
        "Too many incorrect attempts. Request a new code.");

    public static readonly AppError InvalidRefreshToken =
        new("invalid_refresh_token", ErrorKind.Unauthorized, "The session has ended. Sign in again.");

    public static readonly AppError AccountNotFound =
        new("account_not_found", ErrorKind.Unauthorized, "The account no longer exists.");

    public static AppError TooManyRequests(TimeSpan retryAfter) => new(
        "too_many_requests",
        ErrorKind.TooManyRequests,
        "Too many codes requested. Wait before trying again.",
        RetryAfter: retryAfter);

    public static AppError InvalidProfile(string detail) => new("invalid_profile", ErrorKind.Validation, detail);

    public static AppError DeletionBlocked(IReadOnlyList<string> reasons) => new(
        "deletion_blocked",
        ErrorKind.Conflict,
        "The account has activity that must finish before it can be deleted.",
        Reasons: reasons);
}
