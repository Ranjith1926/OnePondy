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

public sealed record PublicPlaceSummary(
    Guid Id,
    string Slug,
    string Name,
    string CategoryCode,
    double? Latitude,
    double? Longitude,
    PublicImage? CoverImage);

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

        var total = await places.CountAsync(cancellationToken);
        var pageItems = await places
            .OrderBy(p => p.Name)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

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
                covers.TryGetValue(p.Id, out var cover) ? PublicImage.From(cover, storage) : null))
            .ToList();
        return new PagedResult<PublicPlaceSummary>(items, pageNumber, size, total);
    }

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
