namespace OnePondy.Domain.Places;

/// <summary>A place a customer bookmarked (spec §10, Phase 4). Composite key: one row per user/place.</summary>
public sealed class SavedPlace
{
    private SavedPlace()
    {
    }

    private SavedPlace(Guid userId, Guid placeId, DateTimeOffset now)
    {
        UserId = userId;
        PlaceId = placeId;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    public Guid PlaceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SavedPlace Create(Guid userId, Guid placeId, DateTimeOffset now) => new(userId, placeId, now);
}
