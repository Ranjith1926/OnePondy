using OnePondy.Application.Common;

namespace OnePondy.Application.Itineraries;

public static class ItineraryErrors
{
    public static readonly AppError NotFound =
        new("itinerary_not_found", ErrorKind.NotFound, "That itinerary does not exist.");

    public static readonly AppError StopNotFound =
        new("stop_not_found", ErrorKind.NotFound, "That stop does not exist.");

    public static readonly AppError SharingDisabled =
        new("itinerary_not_shared", ErrorKind.NotFound, "That link is no longer active.");

    public static readonly AppError PlaceNotAvailable = new(
        "place_not_available",
        ErrorKind.Validation,
        "This place is not available to add right now.");

    public static readonly AppError TooManyStops = new(
        "too_many_stops",
        ErrorKind.Conflict,
        $"An itinerary can have at most {Domain.Itineraries.Itinerary.MaxStops} stops.");
}

public static class SavedPlaceErrors
{
    public static readonly AppError PlaceNotAvailable = new(
        "place_not_available",
        ErrorKind.Validation,
        "This place is not available to save right now.");
}
