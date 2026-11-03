using OnePondy.Application.Common;
using OnePondy.Domain.Rides;

namespace OnePondy.Application.Rides;

public sealed record RaiseSosInput(double? Latitude, double? Longitude);

public sealed record ReportIncidentInput(RideSafetyCategory? Category, string? Details);

/// <summary>What the person who raised an SOS or reported an incident is told back. The emergency number is for them to call: OnePondy staff are not an emergency service.</summary>
public sealed record SafetyReceiptView(Guid Id, RideSafetyKind Kind, RideSafetyStatus Status, DateTimeOffset CreatedAt, string EmergencyNumber);

/// <summary>A new trip-sharing link. The secret is in <c>Token</c> and shown once: only its hash is stored.</summary>
public sealed record ShareLinkView(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// What someone following a shared trip sees: the rider's first name, the driver's first name, class and plate, the names of the two places and, while the
/// ride is active, the driver's position. Once the ride has ended <c>Ended</c> is true and everything else is empty.
/// </summary>
public sealed record SharedRideView(
    bool Ended,
    RideStatus Status,
    string? RiderFirstName,
    string? DriverFirstName,
    RideVehicleClass? VehicleClass,
    string? VehicleNumber,
    string? PickupLabel,
    string? DestinationLabel,
    RideLocationView? DriverLocation,
    DateTimeOffset UpdatedAt);

public sealed record SafetyListQuery(string? View, int? Page, int? PageSize);

/// <summary>One row of the staff's safety queue.</summary>
public sealed record SafetySummary(
    Guid Id,
    Guid RideId,
    RideSafetyKind Kind,
    RideSafetyCategory Category,
    RideParty ReportedBy,
    RideSafetyStatus Status,
    string? Details,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AcknowledgedAt);

/// <summary>One person on a ride, for staff dealing with a report. Contact details are here because staff may need to call; opening the report is audited.</summary>
public sealed record SafetyPerson(Guid UserId, string? Name, string? PhoneNumber, string? Email);

public sealed record SafetyRideSummary(
    Guid RideId,
    RideStatus Status,
    RideVehicleClass VehicleClass,
    string PickupLabel,
    string DestinationLabel,
    decimal Fare,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? VehicleNumber);

public sealed record SafetyDetail(
    Guid Id,
    uint Version,
    RideSafetyKind Kind,
    RideSafetyCategory Category,
    RideParty ReportedBy,
    RideSafetyStatus Status,
    string? Details,
    double? Latitude,
    double? Longitude,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AcknowledgedAt,
    Guid? AcknowledgedByUserId,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote,
    SafetyRideSummary Ride,
    SafetyPerson Rider,
    SafetyPerson? Driver);

public sealed record SafetyCounts(int OpenSos, int OpenIncidents, DateTimeOffset? OldestOpenAt);

public sealed record ResolveSafetyInput(uint Version, string? Note);

public static class RideSafetyErrors
{
    public static readonly AppError NotFound = new("ride_safety_not_found", ErrorKind.NotFound, "That report does not exist.");

    public static readonly AppError NotOnRide = new("ride_not_found", ErrorKind.NotFound, "That ride does not exist.");

    public static readonly AppError NotActive = new(
        "ride_not_active",
        ErrorKind.Conflict,
        "The emergency button works while a driver is with you. If you are in danger now, call the emergency number.");

    public static readonly AppError WindowPassed = new(
        "ride_report_window_passed",
        ErrorKind.Conflict,
        "That ride ended too long ago to report here. Contact support.");

    public static readonly AppError TooManyOpen = new(
        "ride_report_limit",
        ErrorKind.Conflict,
        "You already have several reports waiting for us. We will get to them.");

    public static readonly AppError NoDriverYet = new(
        "ride_no_driver_yet",
        ErrorKind.Conflict,
        "There is nobody to report until a driver has taken the ride.");

    public static readonly AppError ShareNotAvailable = new(
        "ride_share_unavailable",
        ErrorKind.Conflict,
        "A trip can be shared while it is on its way, not before a driver has it or after it ends.");

    public static readonly AppError Changed = new(
        "ride_safety_changed",
        ErrorKind.Conflict,
        "This changed since you loaded it. Reload and try again.");

    public static AppError InvalidTransition(string message) => new("ride_safety_invalid_transition", ErrorKind.Conflict, message);
}
