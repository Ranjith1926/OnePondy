namespace OnePondy.Domain.Listings;

public enum ListingType
{
    Activity = 1,
    Stay = 2,
    Event = 3,
    Service = 4,
    Shop = 5,
}

public enum ListingStatus
{
    Draft = 1,
    Pending = 2,
    Published = 3,
    Suspended = 4,
}

/// <param name="FreeCancellationHours">Hours before the slot start that a customer can cancel for a full refund.</param>
/// <param name="RefundPercentage">Refund given outside the free-cancellation window, from 0 to 100.</param>
public sealed record CancellationPolicy(int FreeCancellationHours, int RefundPercentage, string? Notes)
{
    public static CancellationPolicy Validate(CancellationPolicy policy)
    {
        if (policy.FreeCancellationHours < 0)
        {
            throw new ArgumentException("Free cancellation hours cannot be negative.", nameof(policy));
        }

        if (policy.RefundPercentage is < 0 or > 100)
        {
            throw new ArgumentException("Refund percentage must be between 0 and 100.", nameof(policy));
        }

        return policy;
    }
}

public sealed record ListingContent(
    ListingType ListingType,
    string? Title,
    string? Description,
    bool IsBookable,
    decimal? BasePrice,
    CancellationPolicy? CancellationPolicy);

/// <summary>
/// What a partner sells or offers (spec §10, Phase 6): an activity, stay, event or service a customer
/// can book, or a shop or service that is discovery-only (spec D13). Only a listing under an approved,
/// active business can be submitted for review.
/// </summary>
public sealed class Listing
{
    public const int MaxTitleLength = 180;
    public const int MaxDescriptionLength = 2000;

    private Listing()
    {
    }

    public Guid Id { get; private set; }

    public Guid BusinessId { get; private set; }

    public ListingType ListingType { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Only an activity, stay, event or service can be bookable; a shop listing is discovery-only (D13).</summary>
    public bool IsBookable { get; private set; }

    public decimal? BasePrice { get; private set; }

    public string Currency { get; private set; } = "INR";

    public CancellationPolicy? CancellationPolicy { get; private set; }

    public ListingStatus Status { get; private set; }

    /// <summary>The reviewer's reason when sent back to draft or suspended.</summary>
    public string? ReviewNote { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    /// <summary>Only a draft listing can be edited; a submitted, published or suspended one cannot.</summary>
    public bool CanEdit => Status == ListingStatus.Draft;

    public static Listing Create(Guid businessId, ListingContent content, DateTimeOffset now)
    {
        var listing = new Listing
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            Status = ListingStatus.Draft,
            CreatedAt = now,
        };
        listing.UpdateContent(content, now);
        return listing;
    }

    public void UpdateContent(ListingContent content, DateTimeOffset now)
    {
        EnsureState(Status is ListingStatus.Draft, "Only a draft listing can be edited.");
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Title);
        if (content.IsBookable && content.ListingType == ListingType.Shop)
        {
            throw new ArgumentException("A shop listing is discovery-only and cannot be bookable.", nameof(content));
        }

        if (content.IsBookable && content.CancellationPolicy is null)
        {
            throw new ArgumentException("A bookable listing needs a cancellation policy.", nameof(content));
        }

        ListingType = content.ListingType;
        Title = content.Title.Trim();
        Description = content.Description;
        IsBookable = content.IsBookable;
        BasePrice = content.BasePrice;
        CancellationPolicy = content.CancellationPolicy is { } policy ? Listings.CancellationPolicy.Validate(policy) : null;
        ReviewNote = null;
        UpdatedAt = now;
    }

    public void Submit(DateTimeOffset now)
    {
        EnsureState(Status == ListingStatus.Draft, "Only a draft listing can be submitted.");
        Status = ListingStatus.Pending;
        UpdatedAt = now;
    }

    public void Publish(DateTimeOffset now)
    {
        EnsureState(Status == ListingStatus.Pending, "Only a pending listing can be published.");
        Status = ListingStatus.Published;
        ReviewNote = null;
        UpdatedAt = now;
    }

    /// <summary>Sends a pending listing back to the partner for changes.</summary>
    public void SendBackToDraft(string reason, DateTimeOffset now)
    {
        EnsureState(Status == ListingStatus.Pending, "Only a pending listing can be sent back.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = ListingStatus.Draft;
        ReviewNote = reason.Trim();
        UpdatedAt = now;
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        EnsureState(Status == ListingStatus.Published, "Only a published listing can be suspended.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = ListingStatus.Suspended;
        ReviewNote = reason.Trim();
        UpdatedAt = now;
    }

    public void Reinstate(DateTimeOffset now)
    {
        EnsureState(Status == ListingStatus.Suspended, "Only a suspended listing can be reinstated.");
        Status = ListingStatus.Published;
        ReviewNote = null;
        UpdatedAt = now;
    }

    private static void EnsureState(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
