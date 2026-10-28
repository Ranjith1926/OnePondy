using OnePondy.Application.Common;

namespace OnePondy.Application.Bookings;

public static class BookingErrors
{
    public static readonly AppError NotFound = new("booking_not_found", ErrorKind.NotFound, "That booking does not exist.");

    public static readonly AppError ListingNotBookable = new(
        "listing_not_bookable",
        ErrorKind.Validation,
        "This listing cannot be booked.");

    public static readonly AppError SlotNotFound = new("slot_not_found", ErrorKind.NotFound, "That availability slot does not exist.");

    public static readonly AppError NotEnoughAvailability = new(
        "not_enough_availability",
        ErrorKind.Conflict,
        "There is not enough availability left. Check availability and try again.");

    public static readonly AppError Changed = new(
        "booking_changed",
        ErrorKind.Conflict,
        "This booking changed since you loaded it. Reload and try again.");

    public static AppError InvalidTransition(string message) => new("booking_invalid_transition", ErrorKind.Conflict, message);
}
