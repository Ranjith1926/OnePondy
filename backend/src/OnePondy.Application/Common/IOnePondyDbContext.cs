using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Itineraries;
using OnePondy.Domain.Listings;
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

    /// <summary>For changes that span several saves, such as Identity role updates plus their audit entry.</summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
