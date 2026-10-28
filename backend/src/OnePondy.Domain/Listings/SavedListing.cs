namespace OnePondy.Domain.Listings;

/// <summary>A customer's bookmark on a listing (spec §10, Phase 6), the listing equivalent of <c>SavedPlace</c>.</summary>
public sealed class SavedListing
{
    private SavedListing()
    {
    }

    public Guid UserId { get; private set; }

    public Guid ListingId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SavedListing Create(Guid userId, Guid listingId, DateTimeOffset now) => new()
    {
        UserId = userId,
        ListingId = listingId,
        CreatedAt = now,
    };
}
