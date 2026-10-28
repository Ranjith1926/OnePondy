using OnePondy.Application.Common;
using OnePondy.Domain.Listings;

namespace OnePondy.Application.Listings;

/// <summary>Already-parsed input for creating or editing a listing (the API layer parses <see cref="ListingType"/> from its camelCase string).</summary>
public sealed record ListingApplicationInput(
    ListingType ListingType,
    string? Title,
    string? Description,
    bool IsBookable,
    decimal? BasePrice,
    int? FreeCancellationHours,
    int? RefundPercentage,
    string? CancellationNotes);

public static class ListingInputValidator
{
    public static Result<ListingContent> Validate(ListingApplicationInput input)
    {
        var errors = new FieldErrors();
        var listingType = input.ListingType;
        var title = errors.Required("title", input.Title, Listing.MaxTitleLength);
        var description = errors.Optional("description", input.Description, Listing.MaxDescriptionLength);

        if (input.IsBookable && listingType == ListingType.Shop)
        {
            errors.Add("isBookable", "A shop listing is discovery-only and cannot be bookable.");
        }

        if (input.BasePrice is { } price && price < 0)
        {
            errors.Add("basePrice", "Enter a price of 0 or more.");
        }

        CancellationPolicy? policy = null;
        if (input.IsBookable)
        {
            if (input.FreeCancellationHours is not { } hours || hours < 0)
            {
                errors.Add("freeCancellationHours", "Enter 0 or more hours.");
            }
            else if (input.RefundPercentage is not { } refund || refund is < 0 or > 100)
            {
                errors.Add("refundPercentage", "Enter a percentage between 0 and 100.");
            }
            else
            {
                policy = new CancellationPolicy(hours, refund, errors.Optional("cancellationNotes", input.CancellationNotes, 500));
            }
        }

        return errors.Any
            ? errors.ToError()
            : new ListingContent(listingType, title, description, input.IsBookable, input.BasePrice, policy);
    }
}
