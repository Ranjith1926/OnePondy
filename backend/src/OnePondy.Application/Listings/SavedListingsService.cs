using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Domain.Listings;

namespace OnePondy.Application.Listings;

public sealed record SavedListingView(PublicListingSummary Listing, DateTimeOffset SavedAt);

public static class SavedListingErrors
{
    public static readonly AppError ListingNotAvailable = new(
        "listing_not_available",
        ErrorKind.Validation,
        "Only a listing that is live in the app can be saved.");
}

/// <summary>A customer's bookmarked listings (spec §10, Phase 6): saving needs an account (spec §4 Guest access).</summary>
public sealed class SavedListingsService(IOnePondyDbContext db, TimeProvider time)
{
    public async Task<IReadOnlyList<SavedListingView>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var saved = await db.SavedListings
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        // A listing that was suspended since it was saved is hidden rather than shown stale.
        var listingIds = saved.Select(s => s.ListingId).ToList();
        var listings = await db.Listings.AsNoTracking()
            .Where(l => listingIds.Contains(l.Id) && l.Status == ListingStatus.Published)
            .ToDictionaryAsync(l => l.Id, cancellationToken);
        var businessIds = listings.Values.Select(l => l.BusinessId).Distinct().ToList();
        var businesses = await db.Businesses.AsNoTracking()
            .Where(b => businessIds.Contains(b.Id) && b.IsActive)
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        return
        [
            .. saved
                .Where(s => listings.TryGetValue(s.ListingId, out var listing) && businesses.ContainsKey(listing.BusinessId))
                .Select(s =>
                {
                    var listing = listings[s.ListingId];
                    return new SavedListingView(
                        new PublicListingSummary(
                            listing.Id,
                            businesses[listing.BusinessId].Name,
                            listing.ListingType,
                            listing.Title,
                            listing.IsBookable,
                            listing.BasePrice,
                            listing.Currency),
                        s.CreatedAt);
                }),
        ];
    }

    /// <summary>Saving an already-saved listing succeeds without changing anything.</summary>
    public async Task<AppError?> SaveAsync(Guid userId, Guid listingId, CancellationToken cancellationToken)
    {
        if (await db.SavedListings.AsNoTracking().AnyAsync(s => s.UserId == userId && s.ListingId == listingId, cancellationToken))
        {
            return null;
        }

        var listing = await db.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listingId, cancellationToken);
        if (listing is null
            || listing.Status != ListingStatus.Published
            || !await db.Businesses.AsNoTracking().AnyAsync(b => b.Id == listing.BusinessId && b.IsActive, cancellationToken))
        {
            return SavedListingErrors.ListingNotAvailable;
        }

        db.SavedListings.Add(SavedListing.Create(userId, listingId, time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    /// <summary>Removing an unsaved listing succeeds without changing anything.</summary>
    public async Task RemoveAsync(Guid userId, Guid listingId, CancellationToken cancellationToken)
    {
        var saved = await db.SavedListings.SingleOrDefaultAsync(s => s.UserId == userId && s.ListingId == listingId, cancellationToken);
        if (saved is null)
        {
            return;
        }

        db.SavedListings.Remove(saved);
        await db.SaveChangesAsync(cancellationToken);
    }
}
