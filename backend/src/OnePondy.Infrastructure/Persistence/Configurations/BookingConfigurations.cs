using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Application.Bookings;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Listings;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Tables follow the reference schema in spec §17.3 (Migration 2).

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    // booking_type excludes 'shop': a shop listing can never be bookable (ck_listing_bookable_type).
    private static readonly string BookableTypesSqlList = string.Join(
        ", ",
        new[] { ListingType.Activity, ListingType.Stay, ListingType.Event, ListingType.Service }
            .Select(t => $"'{SnakeCaseEnum<ListingType>.ToDatabase(t)}'"));

    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", table =>
        {
            table.HasCheckConstraint("ck_booking_type", $"booking_type IN ({BookableTypesSqlList})");
            table.HasCheckConstraint("ck_booking_status", $"status IN ({SnakeCaseEnum<BookingStatus>.SqlList})");
            table.HasCheckConstraint(
                "ck_booking_hold",
                $"status NOT IN ('{SnakeCaseEnum<BookingStatus>.ToDatabase(BookingStatus.Pending)}', '{SnakeCaseEnum<BookingStatus>.ToDatabase(BookingStatus.AwaitingPayment)}') OR expires_at IS NOT NULL");
            table.HasCheckConstraint("ck_booking_guest_count", "guest_count > 0");
            table.HasCheckConstraint(
                "ck_booking_amounts",
                "subtotal >= 0 AND fees >= 0 AND tax_amount >= 0 AND discount_amount >= 0 AND total_amount >= 0");
            table.HasCheckConstraint("ck_booking_total", "total_amount = subtotal + fees + tax_amount - discount_amount");
            table.HasCheckConstraint("ck_booking_dates", "end_at IS NULL OR start_at IS NULL OR end_at > start_at");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.BookingType).HasConversion(SnakeCaseEnum<ListingType>.Converter).HasMaxLength(24).HasColumnName("booking_type");
        builder.Property(b => b.Status).HasConversion(SnakeCaseEnum<BookingStatus>.Converter).HasMaxLength(32);
        builder.Property(b => b.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(b => b.GuestCount);
        builder.Property(b => b.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(b => b.Subtotal).HasPrecision(12, 2);
        builder.Property(b => b.Fees).HasPrecision(12, 2);
        builder.Property(b => b.TaxAmount).HasPrecision(12, 2);
        builder.Property(b => b.DiscountAmount).HasPrecision(12, 2);
        builder.Property(b => b.TotalAmount).HasPrecision(12, 2);
        builder.Property(b => b.PriceSnapshot)
            .HasConversion(PriceSnapshotJson.Converter, PriceSnapshotJson.Comparer)
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(b => b.CancellationPolicySnapshot)
            .HasConversion(CancellationPolicyJson.RequiredConverter, CancellationPolicyJson.RequiredComparer)
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(b => b.CancellationReason).HasMaxLength(BookingService.MaxReasonLength);
        builder.Property(b => b.Version).IsRowVersion();

        // One foreign key for both columns, so a booking cannot point at another business's listing.
        builder.HasOne<Listing>()
            .WithMany()
            .HasForeignKey(b => new { b.ListingId, b.BusinessId })
            .HasPrincipalKey("Id", "BusinessId")
            .OnDelete(DeleteBehavior.Restrict);

        // Bookings outlive the customer's account, which is anonymised rather than deleted (spec §17.3).
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.CustomerUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => new { b.CustomerUserId, b.IdempotencyKey }).IsUnique().HasDatabaseName("ux_bookings_idempotency");
        builder.HasIndex(b => new { b.CustomerUserId, b.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_bookings_customer_created");
        builder.HasIndex(b => new { b.BusinessId, b.Status, b.StartAt }).HasDatabaseName("ix_bookings_business_status");
        builder.HasIndex(b => b.ExpiresAt)
            .HasDatabaseName("ix_bookings_hold_expiry")
            .HasFilter($"status IN ('{SnakeCaseEnum<BookingStatus>.ToDatabase(BookingStatus.Pending)}', '{SnakeCaseEnum<BookingStatus>.ToDatabase(BookingStatus.AwaitingPayment)}')");

        builder.OwnsMany(b => b.History, history =>
        {
            history.ToTable("booking_status_history", table =>
            {
                table.HasCheckConstraint(
                    "ck_booking_history_status",
                    $"new_status IN ({SnakeCaseEnum<BookingStatus>.SqlList}) AND (previous_status IS NULL OR previous_status IN ({SnakeCaseEnum<BookingStatus>.SqlList}))");
                table.HasCheckConstraint("ck_booking_history_actor_type", $"changed_by_type IN ({SnakeCaseEnum<BookingActorType>.SqlList})");
                table.HasCheckConstraint(
                    "ck_booking_history_actor",
                    $"(changed_by_type = '{SnakeCaseEnum<BookingActorType>.ToDatabase(BookingActorType.System)}') = (changed_by_user_id IS NULL)");
            });
            // Append-only and never cascade-deleted (spec §17.3). In the database the foreign key is
            // RESTRICT and a trigger rejects UPDATE and DELETE (migration HardenBookingSchema); EF
            // cannot record a non-cascade ownership key in its model snapshot, so that part lives in SQL.
            history.WithOwner().HasForeignKey("BookingId");
            history.HasOne<ApplicationUser>().WithMany().HasForeignKey(h => h.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            history.HasKey(h => h.Id);
            history.Property(h => h.Id).ValueGeneratedNever();
            history.Property(h => h.PreviousStatus).HasConversion(SnakeCaseEnum<BookingStatus>.Converter).HasMaxLength(32).HasColumnName("previous_status");
            history.Property(h => h.NewStatus).HasConversion(SnakeCaseEnum<BookingStatus>.Converter).HasMaxLength(32).HasColumnName("new_status");
            history.Property(h => h.ActorType).HasConversion(SnakeCaseEnum<BookingActorType>.Converter).HasMaxLength(16).HasColumnName("changed_by_type");
            history.Property(h => h.ActorUserId).HasColumnName("changed_by_user_id");
            history.Property(h => h.Reason).HasMaxLength(BookingService.MaxReasonLength);
            history.HasIndex("BookingId", nameof(BookingStatusHistoryEntry.CreatedAt)).HasDatabaseName("ix_booking_status_history_booking_time");
        });
        builder.Navigation(b => b.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class BookingSlotConfiguration : IEntityTypeConfiguration<BookingSlot>
{
    public void Configure(EntityTypeBuilder<BookingSlot> builder)
    {
        builder.ToTable("booking_slots", table => table.HasCheckConstraint("ck_booking_slot_quantity", "quantity > 0"));
        builder.HasKey(bs => new { bs.BookingId, bs.SlotId });
        builder.HasOne<Booking>().WithMany().HasForeignKey(bs => bs.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AvailabilitySlot>().WithMany().HasForeignKey(bs => bs.SlotId).OnDelete(DeleteBehavior.Restrict);
    }
}
