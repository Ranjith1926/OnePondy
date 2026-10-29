using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Food;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Itineraries;
using OnePondy.Domain.Listings;
using OnePondy.Domain.Notifications;
using OnePondy.Domain.Payments;
using OnePondy.Domain.Places;
using OnePondy.Domain.TripPlanner;

namespace OnePondy.Application.Common;

/// <summary>The persistence surface the application layer uses; implemented by Infrastructure.</summary>
public interface IOnePondyDbContext
{
    DbSet<OtpChallenge> OtpChallenges { get; }

    DbSet<UserSession> UserSessions { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Place> Places { get; }

    DbSet<PlaceCategory> PlaceCategories { get; }

    DbSet<PlaceMedia> PlaceMedia { get; }

    DbSet<ModuleFlag> ModuleFlags { get; }

    DbSet<AppSetting> AppSettings { get; }

    DbSet<AuditEntry> AuditEntries { get; }

    DbSet<SavedPlace> SavedPlaces { get; }

    DbSet<Itinerary> Itineraries { get; }

    DbSet<TripPlannerRequestLog> TripPlannerRequestLogs { get; }

    DbSet<Business> Businesses { get; }

    DbSet<Listing> Listings { get; }

    DbSet<AvailabilitySlot> AvailabilitySlots { get; }

    DbSet<SavedListing> SavedListings { get; }

    DbSet<Booking> Bookings { get; }

    DbSet<BookingSlot> BookingSlots { get; }

    DbSet<PaymentAttempt> PaymentAttempts { get; }

    DbSet<Refund> Refunds { get; }

    DbSet<GatewayEvent> GatewayEvents { get; }

    DbSet<LedgerEntry> LedgerEntries { get; }

    DbSet<RestaurantProfile> RestaurantProfiles { get; }

    DbSet<RestaurantDeliveryArea> RestaurantDeliveryAreas { get; }

    DbSet<MenuCategory> MenuCategories { get; }

    DbSet<MenuItem> MenuItems { get; }

    DbSet<Settlement> Settlements { get; }

    DbSet<SettlementItem> SettlementItems { get; }

    DbSet<ReconciliationException> ReconciliationExceptions { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<DeviceToken> DeviceTokens { get; }

    DbSet<NotificationLogEntry> NotificationLog { get; }

    /// <summary>
    /// Makes the next save fail with a concurrency error unless the row still has this version, so an
    /// edit based on stale data is rejected instead of overwriting someone else's change. Works for
    /// any entity with a <c>Version</c> row-version property, real or shadow.
    /// </summary>
    void ExpectVersion<TEntity>(TEntity entity, uint version)
        where TEntity : class;

    /// <summary>
    /// Tells the next save this entity is new, for one added directly to an owned collection (such as
    /// <see cref="Itinerary.Stops"/>) on an already-tracked parent: change detection alone can mark
    /// such an entity Modified instead of Added, since it has no earlier snapshot to diff against.
    /// </summary>
    void MarkAdded<TEntity>(TEntity entity)
        where TEntity : class;

    /// <summary>
    /// Forgets every tracked change. Used after a save that failed part way, so a half-applied change cannot be saved
    /// along with the next, unrelated one in a job that handles many items with one context.
    /// </summary>
    void ClearTracked();

    /// <summary>For changes that span several saves, such as Identity role updates plus their audit entry.</summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
