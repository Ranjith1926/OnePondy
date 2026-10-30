using OnePondy.Application.Common;
using OnePondy.Domain.Delivery;

namespace OnePondy.Application.Delivery;

/// <summary>
/// A driver's own view of their application and duty. <c>Configured</c> is false until they have applied, and then everything else
/// is empty; the app uses that to show onboarding instead of the home screen.
/// </summary>
public sealed record DriverProfileView(
    bool Configured,
    Guid UserId,
    string? FullName,
    VehicleType? VehicleType,
    string? VehicleNumber,
    string? LicenceNumber,
    DriverApprovalStatus? ApprovalStatus,
    string? ReviewNote,
    bool IsOnDuty,
    DateTimeOffset? OnDutySince,
    bool CanGoOnDuty,
    uint Version);

/// <summary>What a driver sends to apply or to change their details. <c>Version</c> is required once they have applied.</summary>
public sealed record DriverInput(string? FullName, VehicleType? VehicleType, string? VehicleNumber, string? LicenceNumber, uint? Version);

public sealed record DriverListQuery(DriverApprovalStatus? Status, int? Page, int? PageSize);

/// <summary>One row of the administrators' list of drivers.</summary>
public sealed record DriverSummary(
    Guid UserId,
    string FullName,
    VehicleType VehicleType,
    DriverApprovalStatus ApprovalStatus,
    bool IsOnDuty,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// What an administrator reviews: the application, plus how to reach the driver (the phone or email they signed in with, which is
/// shown to staff only).
/// </summary>
public sealed record DriverDetail(
    Guid UserId,
    uint Version,
    string FullName,
    VehicleType VehicleType,
    string? VehicleNumber,
    string? LicenceNumber,
    DriverApprovalStatus ApprovalStatus,
    string? ReviewNote,
    bool IsOnDuty,
    DateTimeOffset? OnDutySince,
    string? PhoneNumber,
    string? Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public static class DriverErrors
{
    public static readonly AppError NotFound = new("driver_not_found", ErrorKind.NotFound, "That driver does not exist.");

    public static readonly AppError NotApplied = new(
        "driver_not_applied",
        ErrorKind.NotFound,
        "Apply to deliver with OnePondy first.");

    public static readonly AppError NotApproved = new(
        "driver_not_approved",
        ErrorKind.Conflict,
        "You can go on duty once OnePondy has approved your application.");

    public static readonly AppError Changed = new(
        "driver_changed",
        ErrorKind.Conflict,
        "This changed since you loaded it. Reload and try again.");

    public static readonly AppError Locked = new(
        "driver_locked",
        ErrorKind.Conflict,
        "Your account is suspended, so you can't change anything here. Contact support.");

    public static readonly AppError LicenceInUse = new(
        "driver_licence_in_use",
        ErrorKind.Conflict,
        "That licence number can't be used. If you think this is a mistake, contact support.");

    public static AppError InvalidTransition(string message) => new("driver_invalid_transition", ErrorKind.Conflict, message);
}
