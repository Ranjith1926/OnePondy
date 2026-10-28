using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Application.Itineraries;
using OnePondy.Domain.Places;

namespace OnePondy.Application.Places;

/// <summary>A customer's bookmarked places (spec §4 Guest access: saved places need an account).</summary>
public sealed class SavedPlacesService(IOnePondyDbContext db, PlaceCatalogService catalog, TimeProvider time)
{
    public async Task<IReadOnlyList<SavedPlaceView>> ListAsync(Guid userId, string? language, CancellationToken cancellationToken)
    {
        var saved = await db.SavedPlaces
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        var places = await catalog.GetSummariesByIdAsync([.. saved.Select(s => s.PlaceId)], language, cancellationToken);
        return
        [
            .. saved
                .Where(s => places.ContainsKey(s.PlaceId))
                .Select(s => new SavedPlaceView(places[s.PlaceId], s.CreatedAt)),
        ];
    }

    /// <summary>Saving an already-saved place succeeds without changing anything.</summary>
    public async Task<AppError?> SaveAsync(Guid userId, Guid placeId, CancellationToken cancellationToken)
    {
        if (await db.SavedPlaces.AsNoTracking().AnyAsync(s => s.UserId == userId && s.PlaceId == placeId, cancellationToken))
        {
            return null;
        }

        if (!await catalog.IsVisibleAsync(placeId, cancellationToken))
        {
            return SavedPlaceErrors.PlaceNotAvailable;
        }

        db.SavedPlaces.Add(SavedPlace.Create(userId, placeId, time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    /// <summary>Removing an unsaved place succeeds without changing anything.</summary>
    public async Task RemoveAsync(Guid userId, Guid placeId, CancellationToken cancellationToken)
    {
        var saved = await db.SavedPlaces.SingleOrDefaultAsync(s => s.UserId == userId && s.PlaceId == placeId, cancellationToken);
        if (saved is null)
        {
            return;
        }

        db.SavedPlaces.Remove(saved);
        await db.SaveChangesAsync(cancellationToken);
    }
}
