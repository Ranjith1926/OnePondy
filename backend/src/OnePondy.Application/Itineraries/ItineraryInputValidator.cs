using OnePondy.Application.Common;
using OnePondy.Domain.Itineraries;

namespace OnePondy.Application.Itineraries;

public static class ItineraryInputValidator
{
    public static Result<(string Title, DateRange Dates)> Validate(ItineraryInput input)
    {
        var errors = new FieldErrors();
        var title = errors.Required("title", input.Title, Itinerary.MaxTitleLength);
        var dates = new DateRange(input.StartDate, input.EndDate);
        if (!dates.IsValid)
        {
            errors.Add("endDate", "The end date cannot be before the start date.");
        }

        return errors.Any ? errors.ToError() : (title, dates);
    }
}
