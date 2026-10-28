using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Application.Places;
using OnePondy.Domain.Itineraries;

namespace OnePondy.Application.Itineraries;

/// <summary>
/// The manual itinerary builder (spec §10, Phase 4): create, edit, reorder stops by day, and share
/// by an unguessable link that shows the plan but never the owner. AI-assisted itineraries (Phase 5)
/// use the same aggregate, starting as drafts instead of saved.
/// </summary>
public sealed class ItineraryService(IOnePondyDbContext db, PlaceCatalogService catalog, TimeProvider time)
{
    public async Task<IReadOnlyList<ItinerarySummary>> ListAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken)
    {
        // Stops are an owned collection, so EF Core loads them with the itinerary automatically.
        var query = db.Itineraries.AsNoTracking().Where(i => i.UserId == userId);
        if (!includeArchived)
        {
            query = query.Where(i => i.Status != ItineraryStatus.Archived);
        }

        var itineraries = await query.OrderByDescending(i => i.UpdatedAt).ToListAsync(cancellationToken);
        return [.. itineraries.Select(ToSummary)];
    }

    public async Task<Result<ItineraryDetail>> GetAsync(Guid id, Guid userId, string? language, CancellationToken cancellationToken)
    {
        var itinerary = await Find(id, userId, cancellationToken);
        return itinerary is null ? ItineraryErrors.NotFound : await ToDetailAsync(itinerary, language, cancellationToken);
    }

    public async Task<Result<ItineraryDetail>> CreateAsync(Guid userId, ItineraryInput input, string? language, CancellationToken cancellationToken)
    {
        var validated = ItineraryInputValidator.Validate(input);
        if (!validated.IsSuccess)
        {
            return validated.Error;
        }

        var itinerary = Itinerary.Create(userId, validated.Value.Title, validated.Value.Dates, ItinerarySource.Manual, time.GetUtcNow());
        db.Itineraries.Add(itinerary);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(itinerary, language, cancellationToken);
    }

    /// <summary>
    /// Creates an AI-assisted draft with its stops already placed (spec Phase 5). The trip planner has
    /// already restricted and validated these stops against published, verified places, so they are
    /// trusted here.
    /// </summary>
    public async Task<ItineraryDetail> CreateAiDraftAsync(
        Guid userId,
        string title,
        DateRange dates,
        IReadOnlyList<(Guid PlaceId, int DayNumber)> stops,
        string? language,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var itinerary = Itinerary.Create(userId, title, dates, ItinerarySource.AiAssisted, now);
        foreach (var stop in stops)
        {
            var added = itinerary.AddStop(stop.PlaceId, stop.DayNumber, notes: null, plannedArrival: null, now);
            db.MarkAdded(added);
        }

        db.Itineraries.Add(itinerary);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(itinerary, language, cancellationToken);
    }

    /// <summary>Moves an AI-assisted draft to saved once the user has reviewed it (spec §7 step 5).</summary>
    public async Task<Result<ItineraryDetail>> ConfirmAsync(Guid id, Guid userId, string? language, CancellationToken cancellationToken)
    {
        var itinerary = await Find(id, userId, cancellationToken);
        if (itinerary is null)
        {
            return ItineraryErrors.NotFound;
        }

        itinerary.Save(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(itinerary, language, cancellationToken);
    }

    public async Task<Result<ItineraryDetail>> UpdateAsync(
        Guid id,
        Guid userId,
        ItineraryInput input,
        string? language,
        CancellationToken cancellationToken)
    {
        var validated = ItineraryInputValidator.Validate(input);
        if (!validated.IsSuccess)
        {
            return validated.Error;
        }

        var itinerary = await Find(id, userId, cancellationToken);
        if (itinerary is null)
        {
            return ItineraryErrors.NotFound;
        }

        itinerary.Rename(validated.Value.Title, validated.Value.Dates, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(itinerary, language, cancellationToken);
    }

    public async Task<AppError?> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var itinerary = await Find(id, userId, cancellationToken);
        if (itinerary is null)
        {
            return ItineraryErrors.NotFound;
        }

        db.Itineraries.Remove(itinerary);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<Result<ItineraryDetail>> SetArchivedAsync(
        Guid id,
        Guid userId,
        bool archived,
        string? language,
        CancellationToken cancellationToken)
    {
        var itinerary = await Find(id, userId, cancellationToken);
        if (itinerary is null)
        {
            return ItineraryErrors.NotFound;
        }

        var now = time.GetUtcNow();
        if (archived)
        {
            itinerary.Archive(now);
        }
        else
        {
            itinerary.Unarchive(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(itinerary, language, cancellationToken);
    }

    public async Task<Result<ItineraryDetail>> AddStopAsync(
        Guid id,
        Guid userId,
        AddStopInput input,
        string? language,
        CancellationToken cancellationToken)
    {
        var itinerary = await Find(id, userId, cancellationToken);
        if (itinerary is null)
        {
            return ItineraryErrors.NotFound;
        }

        if (itinerary.Stops.Count >= Itinerary.MaxStops)
        {
            return ItineraryErrors.TooManyStops;
        }

        if (!await catalog.IsVisibleAsync(input.PlaceId, cancellationToken))
        {
            return ItineraryErrors.PlaceNotAvailable;
        }

        var errors = new FieldErrors();
        var notes = errors.Optional("notes", input.Notes, ItineraryStop.MaxNotesLength);
        var dayNumber = input.DayNumber ?? 1;
        if (dayNumber < 1)
        {
            errors.Add("dayNumber", "Day number must be 1 or more.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var stop = itinerary.AddStop(input.PlaceId, dayNumber, notes, input.PlannedArrival, time.GetUtcNow());

        // A new entry added directly to an owned collection on an already-tracked, Unchanged parent
        // is picked up by change detection, but as Modified rather than Added — there is nothing in
        // its own history to diff against yet. Told explicitly, it inserts correctly either way.
        db.MarkAdded(stop);

        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(itinerary, language, cancellationToken);
    }

    public async Task<Result<ItineraryDetail>> RemoveStopAsync(
        Guid id,
        Guid userId,
        Guid stopId,
        string? language,
        CancellationToken cancellationToken)
    {
        var itinerary = await Find(id, userId, cancellationToken);
        if (itinerary is null)
        {
            return ItineraryErrors.NotFound;
        }

        if (itinerary.Stops.All(s => s.Id != stopId))
        {
            return ItineraryErrors.StopNotFound;
        }

        itinerary.RemoveStop(stopId, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(itinerary, language, cancellationToken);
    }

    /// <summary>
    /// Applies a full new day/order for some or all stops in one request. Every stop id must belong
    /// to this itinerary, and the whole set is applied together (spec §10, Phase 4: reordering never
    /// fails or loses a stop) — the deferred unique constraint lets stops pass through each other's
    /// old positions within the one transaction this save runs in.
    /// </summary>
    public async Task<Result<ItineraryDetail>> ReorderStopsAsync(
        Guid id,
        Guid userId,
        IReadOnlyList<StopPositionInput> positions,
        string? language,
        CancellationToken cancellationToken)
    {
        var itinerary = await Find(id, userId, cancellationToken);
        if (itinerary is null)
        {
            return ItineraryErrors.NotFound;
        }

        try
        {
            itinerary.Reorder([.. positions.Select(p => (p.StopId, p.DayNumber, p.StopOrder))], time.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["positions"] = [exception.Message] });
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(itinerary, language, cancellationToken);
    }

    /// <summary>Turns sharing on (or re-rolls the link) and returns the new token.</summary>
    public async Task<Result<string>> EnableSharingAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var itinerary = await Find(id, userId, cancellationToken);
        if (itinerary is null)
        {
            return ItineraryErrors.NotFound;
        }

        var token = itinerary.EnableSharing(NewShareToken, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<AppError?> DisableSharingAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var itinerary = await Find(id, userId, cancellationToken);
        if (itinerary is null)
        {
            return ItineraryErrors.NotFound;
        }

        itinerary.DisableSharing(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    /// <summary>Public, unauthenticated read of a shared itinerary. Carries no owner information.</summary>
    public async Task<Result<SharedItineraryView>> GetSharedAsync(string token, string? language, CancellationToken cancellationToken)
    {
        var itinerary = await db.Itineraries
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.ShareToken == token, cancellationToken);
        if (itinerary is null)
        {
            return ItineraryErrors.SharingDisabled;
        }

        var stops = await StopViewsAsync(itinerary, language, cancellationToken);
        return new SharedItineraryView(itinerary.Title, itinerary.StartDate, itinerary.EndDate, stops);
    }

    /// <summary>
    /// Returns the already-tracked instance when this unit of work has loaded it before, instead of
    /// querying again: re-querying an aggregate that owns a collection while it is already tracked
    /// does not reliably reconcile with in-memory changes already made to that collection.
    /// </summary>
    private async Task<Itinerary?> Find(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var tracked = db.Itineraries.Local.SingleOrDefault(i => i.Id == id);
        if (tracked is not null)
        {
            return tracked.UserId == userId ? tracked : null;
        }

        return await db.Itineraries.SingleOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken);
    }

    private async Task<ItineraryDetail> ToDetailAsync(Itinerary itinerary, string? language, CancellationToken cancellationToken)
    {
        var stops = await StopViewsAsync(itinerary, language, cancellationToken);
        return new ItineraryDetail(
            itinerary.Id,
            itinerary.Title,
            itinerary.StartDate,
            itinerary.EndDate,
            itinerary.Status,
            itinerary.Source,
            itinerary.ShareToken,
            itinerary.CreatedAt,
            itinerary.UpdatedAt,
            stops);
    }

    private async Task<IReadOnlyList<ItineraryStopView>> StopViewsAsync(Itinerary itinerary, string? language, CancellationToken cancellationToken)
    {
        var places = await catalog.GetSummariesByIdAsync([.. itinerary.Stops.Select(s => s.PlaceId)], language, cancellationToken);
        return
        [
            .. itinerary.Stops
                .Where(s => places.ContainsKey(s.PlaceId))
                .OrderBy(s => s.DayNumber)
                .ThenBy(s => s.StopOrder)
                .Select(s => new ItineraryStopView(s.Id, s.DayNumber, s.StopOrder, s.Notes, s.PlannedArrival, places[s.PlaceId])),
        ];
    }

    private static ItinerarySummary ToSummary(Itinerary itinerary) => new(
        itinerary.Id,
        itinerary.Title,
        itinerary.StartDate,
        itinerary.EndDate,
        itinerary.Status,
        itinerary.Source,
        itinerary.ShareToken is not null,
        itinerary.Stops.Count,
        itinerary.CreatedAt,
        itinerary.UpdatedAt);

    private static string NewShareToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));
}
