using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Places;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Tables follow the reference schema in spec §17.3 (Migration 1). The PostGIS "location" column
// and its spatial index arrive in Phase 4 with nearby search; latitude and longitude are stored now.

public sealed class PlaceCategoryConfiguration : IEntityTypeConfiguration<PlaceCategory>
{
    public void Configure(EntityTypeBuilder<PlaceCategory> builder)
    {
        builder.ToTable("place_categories");
        builder.HasKey(c => c.Code);
        builder.Property(c => c.Code).HasMaxLength(40);
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();

        // The same codes the customer app already uses for its category chips.
        builder.HasData(
            new PlaceCategory("beach", "Beach", 1),
            new PlaceCategory("heritage", "Heritage", 2),
            new PlaceCategory("spiritual", "Spiritual", 3),
            new PlaceCategory("museum", "Museum", 4),
            new PlaceCategory("nature", "Nature", 5));
    }
}

public sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> builder)
    {
        builder.ToTable("places", table =>
        {
            table.HasCheckConstraint("ck_places_coordinates", "(latitude IS NULL) = (longitude IS NULL)");
            table.HasCheckConstraint("ck_places_latitude", "latitude IS NULL OR latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("ck_places_longitude", "longitude IS NULL OR longitude BETWEEN -180 AND 180");
            table.HasCheckConstraint(
                "ck_places_verification_status",
                $"verification_status IN ({SnakeCaseEnum<VerificationStatus>.SqlList})");
            table.HasCheckConstraint(
                "ck_places_verification_provenance",
                "verification_status <> 'verified' OR (verified_by_user_id IS NOT NULL AND verified_at IS NOT NULL AND verification_source IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_places_publish_requires_verified",
                "NOT is_published OR verification_status = 'verified'");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(180).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(Slugs.MaxLength).IsRequired();
        builder.HasIndex(p => p.Slug).IsUnique();
        builder.Property(p => p.CategoryCode).HasMaxLength(40).IsRequired();
        builder.Property(p => p.OpeningHours)
            .HasConversion(OpeningHoursJson.Converter, OpeningHoursJson.Comparer)
            .HasColumnType("jsonb");
        builder.Property(p => p.Status)
            .HasColumnName("verification_status")
            .HasConversion(SnakeCaseEnum<VerificationStatus>.Converter)
            .HasMaxLength(24);
        builder.Property(p => p.VerificationSource).HasMaxLength(500);
        builder.Property(p => p.ReviewNote).HasMaxLength(1000);
        builder.Property(p => p.Version).IsRowVersion();
        builder.Ignore(p => p.Location);

        builder.HasOne<PlaceCategory>().WithMany().HasForeignKey(p => p.CategoryCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.VerifiedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.CategoryCode).HasFilter("is_published").HasDatabaseName("ix_places_public_category");
        builder.HasIndex(p => p.LastReviewedAt).HasFilter("is_published").HasDatabaseName("ix_places_review_due");
        builder.HasIndex(p => new { p.Status, p.UpdatedAt }).HasDatabaseName("ix_places_console_list");

        builder.OwnsMany(p => p.Translations, translation =>
        {
            translation.ToTable("place_translations", table => table.HasCheckConstraint(
                "ck_place_translation_language",
                "language IN ('ta', 'fr')"));
            translation.WithOwner().HasForeignKey("PlaceId");
            translation.HasKey("PlaceId", nameof(PlaceTranslation.Language));
            translation.Property(t => t.Language).HasMaxLength(8);
            translation.Property(t => t.Name).HasMaxLength(180).IsRequired();
        });
        builder.Navigation(p => p.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(p => p.EntryFees, fee =>
        {
            fee.ToTable("place_entry_fees", table =>
            {
                table.HasCheckConstraint(
                    "ck_place_entry_fee_category",
                    $"visitor_category IN ({SnakeCaseEnum<VisitorCategory>.SqlList})");
                table.HasCheckConstraint("ck_place_entry_fee_amount", "amount >= 0");
            });
            fee.WithOwner().HasForeignKey("PlaceId");
            fee.HasKey("PlaceId", nameof(PlaceEntryFee.Category));
            fee.Property(f => f.Category)
                .HasColumnName("visitor_category")
                .HasConversion(SnakeCaseEnum<VisitorCategory>.Converter)
                .HasMaxLength(24);
            fee.Property(f => f.Amount).HasPrecision(12, 2);
            fee.Property<string>("Currency").HasMaxLength(3).IsFixedLength().IsRequired().HasDefaultValue(PlaceEntryFee.Currency);
        });
        builder.Navigation(p => p.EntryFees).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class PlaceMediaConfiguration : IEntityTypeConfiguration<PlaceMedia>
{
    public void Configure(EntityTypeBuilder<PlaceMedia> builder)
    {
        builder.ToTable("place_media", table =>
        {
            table.HasCheckConstraint("ck_place_media_licence", $"licence IN ({SnakeCaseEnum<MediaLicence>.SqlList})");
            table.HasCheckConstraint(
                "ck_place_media_attribution",
                "licence IN ('owned', 'cc0') OR attribution IS NOT NULL");
        });
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.StorageKey).HasMaxLength(200).IsRequired();
        builder.Property(m => m.AltText).HasMaxLength(PlaceMedia.MaxAltTextLength).IsRequired();
        builder.Property(m => m.Caption).HasMaxLength(PlaceMedia.MaxCaptionLength);
        builder.Property(m => m.Licence).HasConversion(SnakeCaseEnum<MediaLicence>.Converter).HasMaxLength(32);
        builder.Property(m => m.Attribution).HasMaxLength(PlaceMedia.MaxAttributionLength);
        builder.Property(m => m.SourceUrl).HasMaxLength(PlaceMedia.MaxSourceUrlLength);

        builder.HasOne<Place>().WithMany().HasForeignKey(m => m.PlaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => new { m.PlaceId, m.SortOrder });
    }
}

public sealed class ModuleFlagConfiguration : IEntityTypeConfiguration<ModuleFlag>
{
    public void Configure(EntityTypeBuilder<ModuleFlag> builder)
    {
        builder.ToTable("module_flags", table => table.HasCheckConstraint(
            "ck_module_flags_status",
            $"status IN ({SnakeCaseEnum<ModuleStatus>.SqlList})"));
        builder.HasKey(f => f.Key);
        builder.Property(f => f.Key).HasMaxLength(40);
        builder.Property(f => f.Status).HasConversion(SnakeCaseEnum<ModuleStatus>.Converter).HasMaxLength(16);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(f => f.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Nothing is launched yet: every module starts as "coming soon" (spec §4).
        builder.HasData(ModuleKeys.All.Select(key => new ModuleFlag(key, ModuleStatus.ComingSoon)));
    }
}

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(80);
        builder.Property(s => s.Value).HasMaxLength(200).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasData(new AppSetting(AppSetting.MinSupportedAppVersion, "0.1.0"));
    }
}

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Action).HasMaxLength(64).IsRequired();
        builder.Property(e => e.EntityType).HasMaxLength(32).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Details).HasColumnType("jsonb");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredAt });
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => e.ActorUserId);
    }
}
