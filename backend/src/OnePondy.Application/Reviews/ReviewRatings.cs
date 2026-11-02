using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Domain.Reviews;

namespace OnePondy.Application.Reviews;

/// <summary>
/// Displayed ratings (spec §7: "computed only from published reviews of eligible transactions"). A review that is waiting, rejected or taken
/// down never counts, and nothing is shown below <see cref="ReviewOptions.MinCountToShow"/> reviews.
/// </summary>
public static class ReviewRatings
{
    /// <summary>The average to one decimal place and the count, or null while there are too few reviews to show.</summary>
    public static RatingSummary? Summary(int count, int sum, int minCountToShow) =>
        count > 0 && count >= minCountToShow ? new RatingSummary(Math.Round(sum / (double)count, 1, MidpointRounding.AwayFromZero), count) : null;

    /// <summary>Ratings of bookable listings, from the bookings customers completed. Listings with too few reviews are left out.</summary>
    public static async Task<Dictionary<Guid, RatingSummary>> ForListingsAsync(
        IOnePondyDbContext db, IReadOnlyCollection<Guid> listingIds, int minCountToShow, CancellationToken cancellationToken)
    {
        if (listingIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Reviews.AsNoTracking()
            .Where(r => r.Status == ReviewStatus.Published && r.ListingId != null && listingIds.Contains(r.ListingId.Value))
            .GroupBy(r => r.ListingId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Rating) })
            .ToListAsync(cancellationToken);
        return ToSummaries(rows.Select(r => (r.Id, r.Count, r.Sum)), minCountToShow);
    }

    /// <summary>Ratings of restaurants, from the orders customers received.</summary>
    public static async Task<Dictionary<Guid, RatingSummary>> ForRestaurantsAsync(
        IOnePondyDbContext db, IReadOnlyCollection<Guid> businessIds, int minCountToShow, CancellationToken cancellationToken)
    {
        if (businessIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Reviews.AsNoTracking()
            .Where(r => r.Status == ReviewStatus.Published && r.FoodOrderId != null && businessIds.Contains(r.BusinessId))
            .GroupBy(r => r.BusinessId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Rating) })
            .ToListAsync(cancellationToken);
        return ToSummaries(rows.Select(r => (r.Id, r.Count, r.Sum)), minCountToShow);
    }

    private static Dictionary<Guid, RatingSummary> ToSummaries(IEnumerable<(Guid Id, int Count, int Sum)> rows, int minCountToShow)
    {
        var summaries = new Dictionary<Guid, RatingSummary>();
        foreach (var (id, count, sum) in rows)
        {
            if (Summary(count, sum, minCountToShow) is { } summary)
            {
                summaries[id] = summary;
            }
        }

        return summaries;
    }
}
