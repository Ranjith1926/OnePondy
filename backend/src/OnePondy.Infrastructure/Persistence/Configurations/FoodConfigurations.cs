using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Food;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Restaurants and menus (spec Phase 8, step 8.1). Money is numeric(12,2) in INR; rules the model states are also CHECK constraints.

public sealed class RestaurantProfileConfiguration : IEntityTypeConfiguration<RestaurantProfile>
{
    public void Configure(EntityTypeBuilder<RestaurantProfile> builder)
    {
        builder.ToTable("restaurant_profiles", table =>
        {
            table.HasCheckConstraint(
                "ck_restaurant_prep_time",
                $"prep_time_minutes BETWEEN {RestaurantProfile.MinPrepMinutes} AND {RestaurantProfile.MaxPrepMinutes}");
            table.HasCheckConstraint("ck_restaurant_min_order", $"min_order_amount BETWEEN 0 AND {RestaurantProfile.MaxOrderAmount}");
        });

        builder.HasKey(p => p.BusinessId);
        builder.Property(p => p.BusinessId).ValueGeneratedNever();
        builder.Property(p => p.MinOrderAmount).HasPrecision(12, 2);
        builder.Property(p => p.Version).IsRowVersion();

        builder.HasOne<Business>().WithOne().HasForeignKey<RestaurantProfile>(p => p.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantDeliveryAreaConfiguration : IEntityTypeConfiguration<RestaurantDeliveryArea>
{
    public void Configure(EntityTypeBuilder<RestaurantDeliveryArea> builder)
    {
        var codes = string.Join(", ", FoodAreas.All.Select(code => $"'{code}'"));
        builder.ToTable("restaurant_delivery_areas", table => table.HasCheckConstraint("ck_delivery_area_code", $"area_code IN ({codes})"));

        builder.HasKey(a => new { a.BusinessId, a.AreaCode });
        builder.Property(a => a.AreaCode).HasMaxLength(32);

        builder.HasOne<RestaurantProfile>().WithMany().HasForeignKey(a => a.BusinessId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MenuCategoryConfiguration : IEntityTypeConfiguration<MenuCategory>
{
    public void Configure(EntityTypeBuilder<MenuCategory> builder)
    {
        builder.ToTable("menu_categories", table => table.HasCheckConstraint("ck_menu_category_position", "position >= 0"));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(MenuCategory.MaxNameLength).IsRequired();
        builder.Property(c => c.Version).IsRowVersion();

        builder.HasOne<Business>().WithMany().HasForeignKey(c => c.BusinessId).OnDelete(DeleteBehavior.Restrict);

        // The service refuses a name that differs only by capitals; this index is the exact-match backstop for two saves at once.
        builder.HasIndex("BusinessId", "Name").IsUnique().HasDatabaseName("ux_menu_categories_name");

        // So a dish's (category_id, business_id) foreign key can never point at another restaurant's category.
        builder.HasAlternateKey(c => new { c.Id, c.BusinessId }).HasName("ux_menu_categories_id_business");
    }
}

public sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("menu_items", table =>
        {
            table.HasCheckConstraint("ck_menu_item_price", $"price > 0 AND price <= {MenuItem.MaxPrice}");
            table.HasCheckConstraint("ck_menu_item_tax", $"tax_rate_percent BETWEEN 0 AND {MenuItem.MaxTaxRatePercent}");
            table.HasCheckConstraint("ck_menu_item_position", "position >= 0");
            table.HasCheckConstraint("ck_menu_item_archived_unavailable", "NOT is_archived OR NOT is_available");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Name).HasMaxLength(MenuItem.MaxNameLength).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(MenuItem.MaxDescriptionLength);
        builder.Property(i => i.Price).HasPrecision(12, 2);
        builder.Property(i => i.TaxRatePercent).HasPrecision(5, 2);
        builder.Property(i => i.Version).IsRowVersion();

        builder.HasOne<Business>().WithMany().HasForeignKey(i => i.BusinessId).OnDelete(DeleteBehavior.Restrict);

        // A dish always belongs to a category of the same restaurant, and never loses it while orders may name the dish.
        builder.HasOne<MenuCategory>()
            .WithMany()
            .HasForeignKey(i => new { i.CategoryId, i.BusinessId })
            .HasPrincipalKey(c => new { c.Id, c.BusinessId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.BusinessId, i.CategoryId }).HasDatabaseName("ix_menu_items_business_category");
    }
}
