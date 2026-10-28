using OnePondy.Domain.Listings;

namespace OnePondy.Application.Listings;

public sealed record ListingSummary(
    Guid Id,
    Guid BusinessId,
    ListingType ListingType,
    string Title,
    bool IsBookable,
    decimal? BasePrice,
    string Currency,
    ListingStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ListingDetail(
    Guid Id,
    uint Version,
    Guid BusinessId,
    ListingType ListingType,
    string Title,
    string? Description,
    bool IsBookable,
    decimal? BasePrice,
    string Currency,
    CancellationPolicy? CancellationPolicy,
    ListingStatus Status,
    string? ReviewNote,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="BusinessName">Shown instead of any internal business id, the same way a place shows no owner.</param>
public sealed record PublicListingSummary(
    Guid Id,
    string BusinessName,
    ListingType ListingType,
    string Title,
    bool IsBookable,
    decimal? BasePrice,
    string Currency);

public sealed record PublicListingDetail(
    Guid Id,
    Guid BusinessId,
    string BusinessName,
    string? BusinessAddress,
    ListingType ListingType,
    string Title,
    string? Description,
    bool IsBookable,
    decimal? BasePrice,
    string Currency,
    CancellationPolicy? CancellationPolicy);

public sealed record AvailabilitySlotView(
    Guid Id,
    Guid ListingId,
    uint Version,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int Capacity,
    int ReservedCount,
    int AvailableCount);
