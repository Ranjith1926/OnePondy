using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Itineraries;
using OnePondy.Domain.Places;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Tables follow the reference schema in spec §17.3 (Migration 1).

public sealed class SavedPlaceConfiguration : IEntityTypeConfiguration<SavedPlace>
{
    public void Configure(EntityTypeBuilder<SavedPlace> builder)
    {
        builder.ToTable("saved_places");
        builder.HasKey(s => new { s.UserId, s.PlaceId });

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Place>().WithMany().HasForeignKey(s => s.PlaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ItineraryConfiguration : IEntityTypeConfiguration<Itinerary>
{
    public void Configure(EntityTypeBuilder<Itinerary> builder)
    {
        builder.ToTable("itineraries", table =>
        {
            table.HasCheckConstraint(
                "ck_itinerary_dates",
                "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
            table.HasCheckConstraint("ck_itinerary_status", $"status IN ({SnakeCaseEnum<ItineraryStatus>.SqlList})");
            table.HasCheckConstraint("ck_itinerary_source", $"source IN ({SnakeCaseEnum<ItinerarySource>.SqlList})");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Title).HasMaxLength(Itinerary.MaxTitleLength).IsRequired();
        builder.Property(i => i.Status).HasConversion(SnakeCaseEnum<ItineraryStatus>.Converter).HasMaxLength(24);
        builder.Property(i => i.Source).HasConversion(SnakeCaseEnum<ItinerarySource>.Converter).HasMaxLength(16);
        builder.Property(i => i.ShareToken).HasMaxLength(64);
        builder.HasIndex(i => i.ShareToken).IsUnique().HasFilter("share_token IS NOT NULL");
        builder.HasIndex(i => i.UserId);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);

        // Owned (not a separate aggregate root): EF Core then always loads stops with their
        // itinerary, the same way Place.Translations/EntryFees work, so the service never needs an
        // explicit Include and the two can never drift out of sync within one unit of work.
        builder.OwnsMany(i => i.Stops, stop =>
        {
            stop.ToTable("itinerary_stops", table =>
            {
                table.HasCheckConstraint("ck_itinerary_stop_day", "day_number >= 1");
                table.HasCheckConstraint("ck_itinerary_stop_order", "stop_order >= 0");
            });
            stop.WithOwner().HasForeignKey("ItineraryId");
            stop.HasKey(s => s.Id);
            stop.Property(s => s.Id).ValueGeneratedNever();
            stop.Property(s => s.Notes).HasMaxLength(ItineraryStop.MaxNotesLength);
            stop.HasOne<Place>().WithMany().HasForeignKey(s => s.PlaceId).OnDelete(DeleteBehavior.Cascade);

            // Real uniqueness is a deferrable constraint added in the migration (spec §17.3), so a
            // reorder can move several stops through transiently-colliding positions in one
            // transaction. This index exists for lookups only.
            stop.HasIndex(s => new { s.ItineraryId, s.DayNumber, s.StopOrder });
        });
        builder.Navigation(i => i.Stops).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
