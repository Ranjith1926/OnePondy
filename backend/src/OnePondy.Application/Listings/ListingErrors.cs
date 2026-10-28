using OnePondy.Application.Common;

namespace OnePondy.Application.Listings;

public static class ListingErrors
{
    public static readonly AppError NotFound = new("listing_not_found", ErrorKind.NotFound, "That listing does not exist.");

    public static readonly AppError Changed = new(
        "listing_changed",
        ErrorKind.Conflict,
        "This listing changed since you loaded it. Reload and try again.");

    public static readonly AppError CannotEdit = new(
        "listing_locked",
        ErrorKind.Conflict,
        "A submitted, published or suspended listing cannot be edited here.");

    public static readonly AppError BusinessNotActive = new(
        "listing_business_not_active",
        ErrorKind.Validation,
        "Only an approved, active business can list or publish anything.");

    public static readonly AppError SlotNotFound = new("slot_not_found", ErrorKind.NotFound, "That availability slot does not exist.");

    public static readonly AppError SlotChanged = new(
        "slot_changed",
        ErrorKind.Conflict,
        "This slot changed since you loaded it. Reload and try again.");

    public static readonly AppError SlotHasReservations = new(
        "slot_has_reservations",
        ErrorKind.Conflict,
        "This slot has bookings against it and cannot be removed.");

    public static AppError InvalidTransition(string message) => new("listing_invalid_transition", ErrorKind.Conflict, message);
}
