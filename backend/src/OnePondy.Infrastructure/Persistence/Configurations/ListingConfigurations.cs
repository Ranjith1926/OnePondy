using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Application.Listings;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Listings;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Tables follow the reference schema in spec §17.3 (Migration 2).

public sealed class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.ToTable("listings", table =>
        {
            table.HasCheckConstraint("ck_listing_type", $"listing_type IN ({SnakeCaseEnum<ListingType>.SqlList})");
            table.HasCheckConstraint("ck_listing_status", $"status IN ({SnakeCaseEnum<ListingStatus>.SqlList})");
            table.HasCheckConstraint("ck_listing_price", "base_price IS NULL OR base_price >= 0");
            table.HasCheckConstraint(
                "ck_listing_bookable_type",
                $"NOT is_bookable OR listing_type <> '{SnakeCaseEnum<ListingType>.ToDatabase(ListingType.Shop)}'");
            table.HasCheckConstraint("ck_listing_bookable_policy", "NOT is_bookable OR cancellation_policy IS NOT NULL");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.ListingType).HasConversion(SnakeCaseEnum<ListingType>.Converter).HasMaxLength(24);
        builder.Property(l => l.Title).HasMaxLength(Listing.MaxTitleLength).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(Listing.MaxDescriptionLength);
        builder.Property(l => l.BasePrice).HasPrecision(12, 2);
        builder.Property(l => l.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(l => l.CancellationPolicy)
            .HasConversion(CancellationPolicyJson.Converter, CancellationPolicyJson.Comparer)
            .HasColumnType("jsonb");
        builder.Property(l => l.Status).HasConversion(SnakeCaseEnum<ListingStatus>.Converter).HasMaxLength(24);
        builder.Property(l => l.ReviewNote).HasMaxLength(ListingService.MaxReasonLength);
        builder.Property(l => l.Version).IsRowVersion();

        builder.HasOne<Business>().WithMany().HasForeignKey(l => l.BusinessId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.BusinessId, l.Status }).HasDatabaseName("ix_listings_business_status");

        // So a booking's (listing_id, business_id) foreign key can never point at another business's listing.
        builder.HasAlternateKey(l => new { l.Id, l.BusinessId }).HasName("ux_listings_id_business");
    }
}

public sealed class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<AvailabilitySlot> builder)
    {
        builder.ToTable("availability_slots", table =>
        {
            table.HasCheckConstraint("ck_slot_dates", "end_at > start_at");
            table.HasCheckConstraint("ck_slot_capacity", "capacity >= 0 AND reserved_count >= 0 AND reserved_count <= capacity");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Version).IsRowVersion();

        builder.HasOne<Listing>().WithMany().HasForeignKey(s => s.ListingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.ListingId, s.StartAt }).IsUnique().HasDatabaseName("ux_slot_listing_start");
    }
}

public sealed class SavedListingConfiguration : IEntityTypeConfiguration<SavedListing>
{
    public void Configure(EntityTypeBuilder<SavedListing> builder)
    {
        builder.ToTable("saved_listings");
        builder.HasKey(s => new { s.UserId, s.ListingId });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Listing>().WithMany().HasForeignKey(s => s.ListingId).OnDelete(DeleteBehavior.Cascade);
    }
}
