using OnePondy.Application.Common;
using OnePondy.Domain.Delivery;

namespace OnePondy.Application.Delivery;

/// <summary>A dish the driver should find in the bag, so they can check it at the counter.</summary>
public sealed record JobItemView(string Name, int Quantity);

/// <summary>
/// What a driver sees of a delivery that is still open. The customer's address and phone are left out until the driver has taken it:
/// an offer shows only the area, so a driver who never accepts never learns where anyone lives.
/// </summary>
public sealed record JobOfferView(
    Guid Id,
    string RestaurantName,
    string? RestaurantAddress,
    string DeliveryArea,
    int ItemCount,
    DateTimeOffset OpenedAt);

/// <summary>The driver's own delivery, with everything needed to collect and hand it over.</summary>
public sealed record JobDetailView(
    Guid Id,
    DeliveryJobStatus Status,
    string RestaurantName,
    string? RestaurantAddress,
    string? RestaurantPhone,
    string DeliveryArea,
    string DeliveryAddress,
    string? DeliveryNote,
    string ContactPhone,
    IReadOnlyList<JobItemView> Items,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? PickedUpAt);

/// <summary>
/// The driver's jobs screen in one call: the delivery they hold (if any) and, while they are free and on duty, the open ones.
/// <c>CanAcceptJobs</c> is false when they are off duty or not approved, which is why <c>Open</c> is then empty.
/// </summary>
public sealed record DriverJobsView(JobDetailView? Current, IReadOnlyList<JobOfferView> Open, bool CanAcceptJobs);

public sealed record LocationInput(double? Latitude, double? Longitude);

/// <summary>
/// How a delivery is going, for the customer who ordered and the restaurant who cooked. It names the driver by first name and
/// vehicle only, and carries no position: the driver's coordinates are used by OnePondy to run the delivery and are not shared.
/// <c>LocationUpdatedAt</c> only says how fresh the driver's last report is.
/// </summary>
public sealed record DeliveryProgressView(
    DeliveryJobStatus Status,
    string? DriverFirstName,
    VehicleType? VehicleType,
    DateTimeOffset? LocationUpdatedAt);

public static class DeliveryErrors
{
    public static readonly AppError NotFound = new("job_not_found", ErrorKind.NotFound, "That delivery does not exist.");

    public static readonly AppError NotAvailable = new(
        "driver_not_available",
        ErrorKind.Conflict,
        "Go on duty to take deliveries.");

    public static readonly AppError Taken = new(
        "job_taken",
        ErrorKind.Conflict,
        "Another driver took this delivery first.");

    public static readonly AppError Changed = new(
        "job_changed",
        ErrorKind.Conflict,
        "This delivery changed. Reload and try again.");

    public static AppError InvalidTransition(string message) => new("job_invalid_transition", ErrorKind.Conflict, message);
}
