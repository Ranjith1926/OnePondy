using OnePondy.Application.Common;

namespace OnePondy.Application.Businesses;

public static class BusinessErrors
{
    public static readonly AppError NotFound = new("business_not_found", ErrorKind.NotFound, "That business does not exist.");

    public static readonly AppError Changed = new(
        "business_changed",
        ErrorKind.Conflict,
        "This business changed since you loaded it. Reload and try again.");

    public static readonly AppError CannotEdit = new(
        "business_locked",
        ErrorKind.Conflict,
        "An approved or suspended business cannot be edited here.");

    public static readonly AppError CannotApprove = new(
        "business_cannot_approve",
        ErrorKind.Validation,
        "This application is not ready to approve. A restaurant needs an FSSAI licence number first.");

    public static readonly AppError DocumentNotFound = new(
        "business_document_not_found",
        ErrorKind.NotFound,
        "That document does not exist.");

    public static AppError InvalidTransition(string message) => new("business_invalid_transition", ErrorKind.Conflict, message);
}
