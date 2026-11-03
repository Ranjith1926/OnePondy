using OnePondy.Application.Common;
using OnePondy.Domain.Rides;

namespace OnePondy.Application.Rides;

/// <summary>What a rider sends to ask for fares: the two places they chose, each with the name they chose it by.</summary>
public sealed record RideQuoteInput(
    double? PickupLatitude,
    double? PickupLongitude,
    string? PickupLabel,
    double? DestinationLatitude,
    double? DestinationLongitude,
    string? DestinationLabel);

/// <summary>One vehicle option with its fare. <c>Available</c> says whether an eligible driver is close enough right now; asking for an unavailable one is refused.</summary>
public sealed record RideQuoteView(
    Guid QuoteId,
    RideVehicleClass VehicleClass,
    decimal DistanceKm,
    decimal Fare,
    string Currency,
    DateTimeOffset ExpiresAt,
    string CancellationTerms,
    bool Available);

public sealed record RideQuotesView(string PickupLabel, string DestinationLabel, IReadOnlyList<RideQuoteView> Options);

public sealed record RequestRideInput(Guid? QuoteId, string? PickupNote);

public sealed record RideDriverCard(string FirstName, RideVehicleClass VehicleClass, string VehicleNumber);

public sealed record RideLocationView(double Latitude, double Longitude, DateTimeOffset At);

public sealed record RidePlaceView(double Latitude, double Longitude, string Label);

/// <summary>
/// A ride as its rider sees it. The driver is a first name and a vehicle, never a phone number or a licence; the driver's position is shown only
/// while the ride is active, and the rider's own pickup note comes back to them as they wrote it.
/// </summary>
public sealed record RideView(
    Guid Id,
    RideStatus Status,
    RideVehicleClass VehicleClass,
    RidePlaceView Pickup,
    RidePlaceView Destination,
    string? PickupNote,
    decimal DistanceKm,
    decimal Fare,
    string Currency,
    string CancellationTerms,
    DateTimeOffset RequestedAt,
    DateTimeOffset SearchEndsAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    RideCancelledBy? CancelledBy,
    string? CancelReason,
    RideDriverCard? Driver,
    RideLocationView? DriverLocation,
    uint Version);

public sealed record CancelRideInput(string? Reason);

/// <summary>
/// A request as a driver sees it before accepting: where it starts (the name the rider gave it, not the exact point), how far that is from the driver,
/// how long the trip is, where it ends, and the fare. Not the rider's name, the exact pickup point or the note.
/// </summary>
public sealed record RideOfferView(
    Guid Id,
    RideVehicleClass VehicleClass,
    string PickupLabel,
    double PickupDistanceKm,
    string DestinationLabel,
    decimal DistanceKm,
    decimal Fare,
    string Currency,
    DateTimeOffset RequestedAt);

/// <summary>A ride the driver has accepted: now the exact pickup point, the note and the rider's first name are shown (never their phone number).</summary>
public sealed record DriverRideView(
    Guid Id,
    RideStatus Status,
    RideVehicleClass VehicleClass,
    string RiderFirstName,
    RidePlaceView Pickup,
    RidePlaceView Destination,
    string? PickupNote,
    decimal DistanceKm,
    decimal Fare,
    string Currency,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? StartedAt,
    uint Version);

/// <summary>The driver's home: the ride they hold, or, when they hold none and are available, the requests they may take.</summary>
public sealed record DriverRidesView(DriverRideView? Current, IReadOnlyList<RideOfferView> Offers, bool Available);

public sealed record CompleteRideInput(decimal? CashCollected);

public static class RideErrors
{
    public static readonly AppError NotFound = new("ride_not_found", ErrorKind.NotFound, "That ride does not exist.");

    public static readonly AppError ModuleOff = new("rides_unavailable", ErrorKind.Conflict, "Rides are not available yet.");

    public static readonly AppError QuoteNotFound = new("ride_quote_not_found", ErrorKind.NotFound, "That fare is no longer available. Ask for a new one.");

    public static readonly AppError QuoteExpired = new("ride_quote_expired", ErrorKind.Conflict, "That fare has expired. Ask for a new one.");

    public static readonly AppError QuoteUsed = new("ride_quote_used", ErrorKind.Conflict, "You already asked for a ride with that fare.");

    public static readonly AppError ClassUnavailable = new("ride_class_unavailable", ErrorKind.Conflict, "That kind of ride is not offered.");

    public static readonly AppError NoDrivers = new(
        "ride_no_drivers",
        ErrorKind.Conflict,
        "There are no drivers near you right now. Try again in a few minutes.");

    public static readonly AppError RiderBusy = new(
        "ride_rider_busy",
        ErrorKind.Conflict,
        "You already have a ride. Finish or cancel it first.");

    public static readonly AppError DriverBusy = new(
        "ride_driver_busy",
        ErrorKind.Conflict,
        "Finish your current trip first.");

    public static readonly AppError NotAvailable = new(
        "ride_driver_not_available",
        ErrorKind.Conflict,
        "You can take rides once you are approved, insured and on duty.");

    public static readonly AppError Taken = new("ride_taken", ErrorKind.Conflict, "Another driver took this ride.");

    public static readonly AppError OwnRide = new("ride_own_request", ErrorKind.Conflict, "You cannot take a ride you asked for yourself.");

    public static readonly AppError TooFar = new("ride_too_far", ErrorKind.Conflict, "This pickup is too far from where you are.");

    public static readonly AppError Changed = new(
        "ride_changed",
        ErrorKind.Conflict,
        "This ride changed. Reload and try again.");

    public static AppError InvalidTransition(string message) => new("ride_invalid_transition", ErrorKind.Conflict, message);
}

/// <summary>
/// Tells the people on a ride that it changed, so their apps refetch. It carries a ride id only: no position, no name, nothing personal. The API
/// supplies one that pushes over SignalR; without it (tests, the Worker) nothing is pushed and the apps simply poll.
/// </summary>
public interface IRideNotifier
{
    Task RideChangedAsync(Guid rideId, Guid riderUserId, Guid? driverUserId, CancellationToken cancellationToken);
}

public sealed class NoRideNotifier : IRideNotifier
{
    public Task RideChangedAsync(Guid rideId, Guid riderUserId, Guid? driverUserId, CancellationToken cancellationToken) => Task.CompletedTask;
}
