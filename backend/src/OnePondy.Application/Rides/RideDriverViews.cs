using OnePondy.Application.Common;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Rides;

namespace OnePondy.Application.Rides;

/// <summary>
/// A driver's own view of their ride application and duty. <c>Configured</c> is false until they have applied, and then everything else is empty.
/// <c>Eligible</c> is whether they may go on duty today (approved and insured); <c>InsuranceEnded</c> tells an approved driver why not.
/// </summary>
public sealed record RideDriverView(
    bool Configured,
    Guid UserId,
    string? FullName,
    RideVehicleClass? VehicleClass,
    string? VehicleNumber,
    string? LicenceNumber,
    string? PermitNumber,
    string? InsurancePolicyNumber,
    DateOnly? InsuranceValidUntil,
    DriverApprovalStatus? ApprovalStatus,
    string? ReviewNote,
    bool IsOnDuty,
    DateTimeOffset? OnDutySince,
    bool Eligible,
    bool InsuranceEnded,
    uint Version);

/// <summary>What a driver sends to apply or to change their details. <c>Version</c> is required once they have applied.</summary>
public sealed record RideDriverInput(
    string? FullName,
    RideVehicleClass? VehicleClass,
    string? VehicleNumber,
    string? LicenceNumber,
    string? PermitNumber,
    string? InsurancePolicyNumber,
    DateOnly? InsuranceValidUntil,
    uint? Version);

public sealed record RideDriverListQuery(DriverApprovalStatus? Status, int? Page, int? PageSize);

/// <summary>One row of the administrators' list of ride drivers.</summary>
public sealed record RideDriverSummary(
    Guid UserId,
    string FullName,
    RideVehicleClass VehicleClass,
    DriverApprovalStatus ApprovalStatus,
    bool IsOnDuty,
    DateOnly InsuranceValidUntil,
    bool InsuranceEnded,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>What an administrator reviews: the application and its documents, and how to reach the driver (staff only).</summary>
public sealed record RideDriverDetail(
    Guid UserId,
    uint Version,
    string FullName,
    RideVehicleClass VehicleClass,
    string VehicleNumber,
    string LicenceNumber,
    string PermitNumber,
    string InsurancePolicyNumber,
    DateOnly InsuranceValidUntil,
    bool InsuranceEnded,
    DriverApprovalStatus ApprovalStatus,
    string? ReviewNote,
    bool IsOnDuty,
    DateTimeOffset? OnDutySince,
    string? PhoneNumber,
    string? Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public static class RideDriverErrors
{
    public static readonly AppError NotFound = new("ride_driver_not_found", ErrorKind.NotFound, "That driver does not exist.");

    public static readonly AppError NotApplied = new("ride_driver_not_applied", ErrorKind.NotFound, "Apply to drive with OnePondy first.");

    public static readonly AppError NotApproved = new(
        "ride_driver_not_approved",
        ErrorKind.Conflict,
        "You can go on duty once OnePondy has approved your application.");

    public static readonly AppError InsuranceEnded = new(
        "ride_driver_insurance_ended",
        ErrorKind.Conflict,
        "Your insurance has ended. Update your policy and OnePondy will check it again.");

    public static readonly AppError Changed = new(
        "ride_driver_changed",
        ErrorKind.Conflict,
        "This changed since you loaded it. Reload and try again.");

    public static readonly AppError Locked = new(
        "ride_driver_locked",
        ErrorKind.Conflict,
        "Your account is suspended, so you can't change anything here. Contact support.");

    public static readonly AppError DocumentInUse = new(
        "ride_driver_document_in_use",
        ErrorKind.Conflict,
        "That licence or registration number can't be used. If you think this is a mistake, contact support.");

    public static readonly AppError HasActiveRide = new(
        "ride_driver_has_active_ride",
        ErrorKind.Conflict,
        "Finish your trip first.");

    public static readonly AppError NotOnDuty = new(
        "ride_driver_not_on_duty",
        ErrorKind.Conflict,
        "Go on duty to share your location.");

    public static AppError InvalidTransition(string message) => new("ride_driver_invalid_transition", ErrorKind.Conflict, message);
}
