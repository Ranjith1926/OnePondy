using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Application.Media;
using OnePondy.Domain.Places;

namespace OnePondy.Application.Places;

public sealed record PublicImage(
    string AltText,
    string? Caption,
    string? Attribution,
    int Width,
    int Height,
    IReadOnlyDictionary<string, string> Urls)
{
    public static PublicImage From(PlaceMedia media, IMediaStorage storage)
    {
        var view = MediaView.From(media, storage);
        return new PublicImage(view.AltText, view.Caption, view.Attribution, view.Width, view.Height, view.Urls);
    }
}

/// <param name="DistanceKm">Straight-line kilometres from the point given as <c>near</c>; null otherwise.</param>
/// <param name="IsCurrentlyPublished">
/// False only where an unpublished place may legitimately still appear, such as a saved place a
/// content editor has since unpublished; lets the app explain why details may be limited.
/// </param>
public sealed record PublicPlaceSummary(
    Guid Id,
    string Slug,
    string Name,
    string CategoryCode,
    double? Latitude,
    double? Longitude,
    PublicImage? CoverImage,
    double? DistanceKm = null,
    bool IsCurrentlyPublished = true);

public sealed record PublicPlaceDetail(
    Guid Id,
    string Slug,
    string Language,
    string Name,
    string CategoryCode,
    string? Description,
    string? VisitorGuidance,
    string? Address,
    double? Latitude,
    double? Longitude,
    OpeningHoursView? OpeningHours,
    IReadOnlyList<EntryFeeView> EntryFees,
    IReadOnlyList<PublicImage> Images,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? LastReviewedAt);

/// <summary>
/// The customer-facing view of places. Only verified, published places are ever returned (spec §10,
/// Phase 3 acceptance criteria); translated text falls back to English field by field.
/// </summary>
public sealed class PlaceCatalogService(IOnePondyDbContext db, IMediaStorage storage)
{
    public async Task<PagedResult<PublicPlaceSummary>> ListAsync(
        string? categoryCode,
        string? search,
        string? language,
        GeoPoint? near,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var places = Visible();
        if (!string.IsNullOrWhiteSpace(categoryCode))
        {
            places = places.Where(p => p.CategoryCode == categoryCode);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            places = places.Where(p =>
                p.Name.ToLower().Contains(term) || p.Translations.Any(t => t.Name.ToLower().Contains(term)));
        }

        int total;
        List<Place> pageItems;
        if (near is null)
        {
            total = await places.CountAsync(cancellationToken);
            pageItems = await places.OrderBy(p => p.Name).Skip((pageNumber - 1) * size).Take(size).ToListAsync(cancellationToken);
        }
        else
        {
            // No PostGIS yet (spec D6 kept the map/location work provider-agnostic for now): sorting by
            // distance is done in memory. Fine at this catalogue's size; revisit before it grows much
            // beyond a single city or this becomes the hot path.
            var all = await places.ToListAsync(cancellationToken);
            var ordered = all.OrderBy(p => DistanceKm(near, p.Latitude, p.Longitude) ?? double.MaxValue).ToList();
            total = ordered.Count;
            pageItems = [.. ordered.Skip((pageNumber - 1) * size).Take(size)];
        }

        var ids = pageItems.Select(p => p.Id).ToList();
        var covers = (await db.PlaceMedia.AsNoTracking().Where(m => ids.Contains(m.PlaceId)).ToListAsync(cancellationToken))
            .GroupBy(m => m.PlaceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.SortOrder).First());

        var lang = Language(language);
        var items = pageItems
            .Select(p => new PublicPlaceSummary(
                p.Id,
                p.Slug,
                Translation(p, lang)?.Name ?? p.Name,
                p.CategoryCode,
                p.Latitude,
                p.Longitude,
                covers.TryGetValue(p.Id, out var cover) ? PublicImage.From(cover, storage) : null,
                near is null ? null : DistanceKm(near, p.Latitude, p.Longitude)))
            .ToList();
        return new PagedResult<PublicPlaceSummary>(items, pageNumber, size, total);
    }

    /// <summary>
    /// Place summaries for ids a user already has (saved, or in an itinerary), regardless of current
    /// publish state: a place is never hard-deleted (only unpublished), and losing a saved place or
    /// itinerary stop because content was unpublished later would be a worse experience than showing
    /// it with <see cref="PublicPlaceSummary.IsCurrentlyPublished"/> false.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, PublicPlaceSummary>> GetSummariesByIdAsync(
        IReadOnlyCollection<Guid> ids,
        string? language,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, PublicPlaceSummary>();
        }

        var places = await db.Places.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        var covers = (await db.PlaceMedia.AsNoTracking().Where(m => ids.Contains(m.PlaceId)).ToListAsync(cancellationToken))
            .GroupBy(m => m.PlaceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.SortOrder).First());

        var lang = Language(language);
        return places.ToDictionary(
            p => p.Id,
            p => new PublicPlaceSummary(
                p.Id,
                p.Slug,
                Translation(p, lang)?.Name ?? p.Name,
                p.CategoryCode,
                p.Latitude,
                p.Longitude,
                covers.TryGetValue(p.Id, out var cover) ? PublicImage.From(cover, storage) : null,
                IsCurrentlyPublished: p.IsPublished && p.Status == VerificationStatus.Verified));
    }

    /// <summary>Whether a place can be newly saved or added to an itinerary (spec §4: only what a guest can already see).</summary>
    public Task<bool> IsVisibleAsync(Guid placeId, CancellationToken cancellationToken) =>
        Visible().AnyAsync(p => p.Id == placeId, cancellationToken);

    /// <summary>Great-circle distance in kilometres; null when the place has no coordinates.</summary>
    private static double? DistanceKm(GeoPoint from, double? latitude, double? longitude)
    {
        if (latitude is not { } lat || longitude is not { } lng)
        {
            return null;
        }

        const double earthRadiusKm = 6371.0;
        var dLat = ToRadians(lat - from.Latitude);
        var dLng = ToRadians(lng - from.Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(ToRadians(from.Latitude)) * Math.Cos(ToRadians(lat)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    /// <param name="idOrSlug">The place id, or its slug for shareable links.</param>
    public async Task<PublicPlaceDetail?> GetAsync(string idOrSlug, string? language, CancellationToken cancellationToken)
    {
        var place = Guid.TryParse(idOrSlug, out var id)
            ? await Visible().SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            : await Visible().SingleOrDefaultAsync(p => p.Slug == idOrSlug, cancellationToken);
        if (place is null)
        {
            return null;
        }

        var images = await db.PlaceMedia
            .AsNoTracking()
            .Where(m => m.PlaceId == place.Id)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);
        var lang = Language(language);
        var translation = Translation(place, lang);

        return new PublicPlaceDetail(
            place.Id,
            place.Slug,
            translation is null ? "en" : lang,
            translation?.Name ?? place.Name,
            place.CategoryCode,
            translation?.Description ?? place.Description,
            translation?.VisitorGuidance ?? place.VisitorGuidance,
            place.Address,
            place.Latitude,
            place.Longitude,
            OpeningHoursView.From(place.OpeningHours),
            EntryFeeView.From(place.EntryFees),
            [.. images.Select(m => PublicImage.From(m, storage))],
            place.VerifiedAt,
            place.LastReviewedAt);
    }

    private IQueryable<Place> Visible() =>
        db.Places.AsNoTracking().Where(p => p.IsPublished && p.Status == VerificationStatus.Verified);

    private static string Language(string? requested) =>
        requested?.Trim().ToLowerInvariant() is { } lang && ContentLanguages.IsTranslation(lang) ? lang : "en";

    private static PlaceTranslation? Translation(Place place, string language) =>
        language == "en" ? null : place.Translations.FirstOrDefault(t => t.Language == language);
}
