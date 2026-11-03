using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Rides;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Rides (spec Phase 9). The transition rules and the "one driver per ride" rule are enforced again by constraints and a trigger in the migration,
// because they are what keep two drivers from sharing a ride and keep a driver's position from being kept after a trip.

public sealed class RideQuoteConfiguration : IEntityTypeConfiguration<RideQuote>
{
    public void Configure(EntityTypeBuilder<RideQuote> builder)
    {
        builder.ToTable("ride_quotes", table =>
        {
            table.HasCheckConstraint("ck_ride_quote_vehicle_class", $"vehicle_class IN ({SnakeCaseEnum<RideVehicleClass>.SqlList})");
            table.HasCheckConstraint("ck_ride_quote_amounts", "fare > 0 AND distance_km > 0");
            table.HasCheckConstraint("ck_ride_quote_expiry", "expires_at > created_at");
            table.HasCheckConstraint(
                "ck_ride_quote_coordinates",
                "pickup_latitude BETWEEN -90 AND 90 AND pickup_longitude BETWEEN -180 AND 180 "
                + "AND destination_latitude BETWEEN -90 AND 90 AND destination_longitude BETWEEN -180 AND 180");
        });

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.VehicleClass).HasConversion(SnakeCaseEnum<RideVehicleClass>.Converter).HasMaxLength(16);
        builder.Property(q => q.PickupLabel).HasMaxLength(RideQuote.MaxLabelLength).IsRequired();
        builder.Property(q => q.DestinationLabel).HasMaxLength(RideQuote.MaxLabelLength).IsRequired();
        builder.Property(q => q.DistanceKm).HasPrecision(8, 1);
        builder.Property(q => q.Fare).HasPrecision(10, 2);
        builder.Property(q => q.Currency).HasMaxLength(3).IsRequired();
        builder.Property(q => q.CancellationTerms).HasMaxLength(500).IsRequired();

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(q => q.RiderUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(q => new { q.RiderUserId, q.CreatedAt }).HasDatabaseName("ix_ride_quotes_rider_created");
    }
}

public sealed class RideConfiguration : IEntityTypeConfiguration<Ride>
{
    public void Configure(EntityTypeBuilder<Ride> builder)
    {
        builder.ToTable("rides", table =>
        {
            table.HasCheckConstraint("ck_ride_status", $"status IN ({SnakeCaseEnum<RideStatus>.SqlList})");
            table.HasCheckConstraint("ck_ride_vehicle_class", $"vehicle_class IN ({SnakeCaseEnum<RideVehicleClass>.SqlList})");
            table.HasCheckConstraint("ck_ride_cancelled_by", $"cancelled_by IS NULL OR cancelled_by IN ({SnakeCaseEnum<RideCancelledBy>.SqlList})");
            table.HasCheckConstraint("ck_ride_amounts", "fare > 0 AND distance_km > 0 AND (cash_collected IS NULL OR cash_collected = fare)");

            // Requested, expired and rider-cancelled-before-anyone-came rides have no driver; a taken or finished ride always says who.
            table.HasCheckConstraint(
                "ck_ride_driver",
                "(status = 'requested' AND driver_user_id IS NULL) "
                + "OR (status IN ('accepted', 'arrived', 'in_progress', 'completed') AND driver_user_id IS NOT NULL) "
                + "OR status IN ('cancelled', 'expired')");

            // Each step has its time.
            table.HasCheckConstraint(
                "ck_ride_times",
                "(status NOT IN ('accepted', 'arrived', 'in_progress', 'completed') OR accepted_at IS NOT NULL) "
                + "AND (status NOT IN ('arrived', 'in_progress', 'completed') OR arrived_at IS NOT NULL) "
                + "AND (status NOT IN ('in_progress', 'completed') OR started_at IS NOT NULL) "
                + "AND (status <> 'completed' OR (completed_at IS NOT NULL AND cash_collected IS NOT NULL)) "
                + "AND (status NOT IN ('cancelled', 'expired') OR (cancelled_at IS NOT NULL AND cancelled_by IS NOT NULL)) "
                + "AND (status IN ('cancelled', 'expired') OR cancelled_at IS NULL)");

            // The driver's position exists only while they hold the ride, complete, and on Earth.
            table.HasCheckConstraint(
                "ck_ride_location_only_while_active",
                "status IN ('accepted', 'arrived', 'in_progress') OR (driver_latitude IS NULL AND driver_longitude IS NULL AND driver_location_at IS NULL)");
            table.HasCheckConstraint(
                "ck_ride_location_complete",
                "(driver_latitude IS NULL) = (driver_longitude IS NULL) AND (driver_latitude IS NULL) = (driver_location_at IS NULL)");
            table.HasCheckConstraint(
                "ck_ride_location_range",
                "(driver_latitude IS NULL OR driver_latitude BETWEEN -90 AND 90) AND (driver_longitude IS NULL OR driver_longitude BETWEEN -180 AND 180)");
            table.HasCheckConstraint(
                "ck_ride_coordinates",
                "pickup_latitude BETWEEN -90 AND 90 AND pickup_longitude BETWEEN -180 AND 180 "
                + "AND destination_latitude BETWEEN -90 AND 90 AND destination_longitude BETWEEN -180 AND 180");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Status).HasConversion(SnakeCaseEnum<RideStatus>.Converter).HasMaxLength(16);
        builder.Property(r => r.VehicleClass).HasConversion(SnakeCaseEnum<RideVehicleClass>.Converter).HasMaxLength(16);
        builder.Property(r => r.CancelledBy).HasConversion(SnakeCaseEnum<RideCancelledBy>.Converter).HasMaxLength(16);
        builder.Property(r => r.PickupLabel).HasMaxLength(RideQuote.MaxLabelLength).IsRequired();
        builder.Property(r => r.DestinationLabel).HasMaxLength(RideQuote.MaxLabelLength).IsRequired();
        builder.Property(r => r.PickupNote).HasMaxLength(Ride.MaxNoteLength);
        builder.Property(r => r.DistanceKm).HasPrecision(8, 1);
        builder.Property(r => r.Fare).HasPrecision(10, 2);
        builder.Property(r => r.CashCollected).HasPrecision(10, 2);
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.CancellationTerms).HasMaxLength(500).IsRequired();
        builder.Property(r => r.CancelReason).HasMaxLength(Ride.MaxReasonLength);
        builder.Property(r => r.Version).IsRowVersion();

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.RiderUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.DriverUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RideQuote>().WithMany().HasForeignKey(r => r.QuoteId).OnDelete(DeleteBehavior.Restrict);

        // A quote becomes at most one ride; a rider has one open ride at a time; a driver holds one ride at a time.
        builder.HasIndex(r => r.QuoteId).IsUnique().HasDatabaseName("ux_rides_quote");
        builder.HasIndex(r => r.RiderUserId).IsUnique().HasDatabaseName("ux_rides_one_open_per_rider")
            .HasFilter("status IN ('requested', 'accepted', 'arrived', 'in_progress')");
        builder.HasIndex(r => r.DriverUserId).IsUnique().HasDatabaseName("ux_rides_one_active_per_driver")
            .HasFilter("status IN ('accepted', 'arrived', 'in_progress')");

        // What dispatch, the expiry job and the history scan.
        builder.HasIndex(r => new { r.Status, r.VehicleClass, r.RequestedAt }).HasDatabaseName("ix_rides_status_class_requested");
        builder.HasIndex(r => new { r.RiderUserId, r.RequestedAt }).HasDatabaseName("ix_rides_rider_requested");
    }
}

// Safety on rides (spec Phase 9, step 9.3). An SOS and an incident are rows the staff queue reads; a share is a hashed secret and an expiry.

public sealed class RideSafetyEventConfiguration : IEntityTypeConfiguration<RideSafetyEvent>
{
    public void Configure(EntityTypeBuilder<RideSafetyEvent> builder)
    {
        builder.ToTable("ride_safety_events", table =>
        {
            table.HasCheckConstraint("ck_ride_safety_kind", $"kind IN ({SnakeCaseEnum<RideSafetyKind>.SqlList})");
            table.HasCheckConstraint("ck_ride_safety_category", $"category IN ({SnakeCaseEnum<RideSafetyCategory>.SqlList})");
            table.HasCheckConstraint("ck_ride_safety_status", $"status IN ({SnakeCaseEnum<RideSafetyStatus>.SqlList})");
            table.HasCheckConstraint("ck_ride_safety_reported_by", $"reported_by IN ({SnakeCaseEnum<RideParty>.SqlList})");

            // An SOS is always the emergency category and carries no words; an incident is never the emergency category and always has words.
            table.HasCheckConstraint(
                "ck_ride_safety_shape",
                "(kind = 'sos' AND category = 'emergency' AND details IS NULL) "
                + "OR (kind = 'incident' AND category <> 'emergency' AND details IS NOT NULL AND latitude IS NULL AND longitude IS NULL)");
            table.HasCheckConstraint(
                "ck_ride_safety_location",
                "(latitude IS NULL) = (longitude IS NULL) AND (latitude IS NULL OR latitude BETWEEN -90 AND 90) AND (longitude IS NULL OR longitude BETWEEN -180 AND 180)");

            // Each step of the report has its person and its time, and a resolution always says what was done.
            table.HasCheckConstraint(
                "ck_ride_safety_steps",
                "((acknowledged_by_user_id IS NULL) = (acknowledged_at IS NULL)) "
                + "AND ((resolved_by_user_id IS NULL) = (resolved_at IS NULL)) "
                + "AND (status <> 'resolved' OR (resolved_at IS NOT NULL AND resolution_note IS NOT NULL)) "
                + "AND (status = 'resolved' OR resolved_at IS NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion(SnakeCaseEnum<RideSafetyKind>.Converter).HasMaxLength(16);
        builder.Property(e => e.Category).HasConversion(SnakeCaseEnum<RideSafetyCategory>.Converter).HasMaxLength(24);
        builder.Property(e => e.Status).HasConversion(SnakeCaseEnum<RideSafetyStatus>.Converter).HasMaxLength(16);
        builder.Property(e => e.ReportedBy).HasConversion(SnakeCaseEnum<RideParty>.Converter).HasMaxLength(16);
        builder.Property(e => e.Details).HasMaxLength(RideSafetyEvent.MaxDetailsLength);
        builder.Property(e => e.ResolutionNote).HasMaxLength(RideSafetyEvent.MaxNoteLength);
        builder.Property(e => e.Version).IsRowVersion();

        builder.HasOne<Ride>().WithMany().HasForeignKey(e => e.RideId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ReportedByUserId).OnDelete(DeleteBehavior.Restrict);

        // What the staff queue scans: open ones, emergencies first, oldest first.
        builder.HasIndex(e => new { e.Status, e.Kind, e.CreatedAt }).HasDatabaseName("ix_ride_safety_events_queue");
        builder.HasIndex(e => e.RideId).HasDatabaseName("ix_ride_safety_events_ride");

        // One open SOS per person per ride: pressing the button twice is one alarm.
        builder.HasIndex(e => new { e.RideId, e.ReportedByUserId }).IsUnique().HasDatabaseName("ux_ride_safety_events_open_sos")
            .HasFilter("kind = 'sos' AND status <> 'resolved'");
    }
}

public sealed class RideShareConfiguration : IEntityTypeConfiguration<RideShare>
{
    public void Configure(EntityTypeBuilder<RideShare> builder)
    {
        builder.ToTable("ride_shares", table =>
        {
            table.HasCheckConstraint("ck_ride_share_expiry", "expires_at > created_at");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.TokenHash).IsRequired();

        builder.HasOne<Ride>().WithMany().HasForeignKey(s => s.RideId).OnDelete(DeleteBehavior.Restrict);

        // The link's secret is found by its hash.
        builder.HasIndex(s => s.TokenHash).IsUnique().HasDatabaseName("ux_ride_shares_token");
        builder.HasIndex(s => s.RideId).HasDatabaseName("ix_ride_shares_ride");
    }
}
