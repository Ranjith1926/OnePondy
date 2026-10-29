using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Application.Food;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Food;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Food orders (spec Phase 8, step 8.2). Prices are copied onto the order and its lines, and the database checks the arithmetic.

public sealed class FoodOrderConfiguration : IEntityTypeConfiguration<FoodOrder>
{
    public void Configure(EntityTypeBuilder<FoodOrder> builder)
    {
        var areas = string.Join(", ", FoodAreas.All.Where(code => code != FoodAreas.WholeCity).Select(code => $"'{code}'"));
        var placed = SnakeCaseEnum<FoodOrderStatus>.ToDatabase(FoodOrderStatus.Placed);
        var awaitingPayment = SnakeCaseEnum<FoodOrderStatus>.ToDatabase(FoodOrderStatus.AwaitingPayment);

        builder.ToTable("food_orders", table =>
        {
            table.HasCheckConstraint("ck_food_order_status", $"status IN ({SnakeCaseEnum<FoodOrderStatus>.SqlList})");
            table.HasCheckConstraint("ck_food_order_hold", $"status NOT IN ('{placed}', '{awaitingPayment}') OR expires_at IS NOT NULL");
            table.HasCheckConstraint("ck_food_order_area", $"delivery_area IN ({areas})");
            table.HasCheckConstraint(
                "ck_food_order_amounts",
                "subtotal > 0 AND delivery_fee >= 0 AND packaging_fee >= 0 AND tax_amount >= 0 AND discount_amount >= 0 AND total_amount > 0");
            table.HasCheckConstraint(
                "ck_food_order_total",
                "total_amount = subtotal + delivery_fee + packaging_fee + tax_amount - discount_amount");
            table.HasCheckConstraint("ck_food_order_prep_time", "prep_time_minutes > 0");
        });

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Status).HasConversion(SnakeCaseEnum<FoodOrderStatus>.Converter).HasMaxLength(32);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(FoodOrder.MaxKeyLength).IsRequired();
        builder.Property(o => o.DeliveryArea).HasMaxLength(32).IsRequired();
        builder.Property(o => o.DeliveryAddress).HasMaxLength(OrderService.MaxAddressLength).IsRequired();
        builder.Property(o => o.DeliveryNote).HasMaxLength(OrderService.MaxNoteLength);
        builder.Property(o => o.ContactPhone).HasMaxLength(16).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(o => o.Subtotal).HasPrecision(12, 2);
        builder.Property(o => o.DeliveryFee).HasPrecision(12, 2);
        builder.Property(o => o.PackagingFee).HasPrecision(12, 2);
        builder.Property(o => o.TaxAmount).HasPrecision(12, 2);
        builder.Property(o => o.DiscountAmount).HasPrecision(12, 2);
        builder.Property(o => o.TotalAmount).HasPrecision(12, 2);
        builder.Property(o => o.ClosedReason).HasMaxLength(FoodOrder.MaxReasonLength);
        builder.Property(o => o.Version).IsRowVersion();

        builder.HasOne<Business>().WithMany().HasForeignKey(o => o.BusinessId).OnDelete(DeleteBehavior.Restrict);

        // Orders outlive the customer's account, which is anonymised rather than deleted.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(o => o.CustomerUserId).OnDelete(DeleteBehavior.Restrict);

        // The same attempt sent twice (a retry) is one order.
        builder.HasIndex(o => new { o.CustomerUserId, o.IdempotencyKey }).IsUnique().HasDatabaseName("ux_food_orders_idempotency");
        builder.HasIndex(o => new { o.CustomerUserId, o.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_food_orders_customer_created");
        builder.HasIndex(o => new { o.BusinessId, o.Status, o.CreatedAt }).HasDatabaseName("ix_food_orders_business_status");
        builder.HasIndex(o => o.ExpiresAt).HasDatabaseName("ix_food_orders_expiry").HasFilter($"status IN ('{placed}', '{awaitingPayment}')");

        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.History).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class FoodOrderLineConfiguration : IEntityTypeConfiguration<FoodOrderLine>
{
    public void Configure(EntityTypeBuilder<FoodOrderLine> builder)
    {
        builder.ToTable("food_order_lines", table =>
        {
            table.HasCheckConstraint("ck_food_order_line_quantity", $"quantity BETWEEN 1 AND {FoodOrderLine.MaxQuantity}");
            table.HasCheckConstraint("ck_food_order_line_price", "unit_price > 0 AND tax_rate_percent >= 0 AND tax_amount >= 0");
            table.HasCheckConstraint("ck_food_order_line_subtotal", "subtotal = unit_price * quantity");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Name).HasMaxLength(MenuItem.MaxNameLength).IsRequired();
        builder.Property(l => l.UnitPrice).HasPrecision(12, 2);
        builder.Property(l => l.TaxRatePercent).HasPrecision(5, 2);
        builder.Property(l => l.Subtotal).HasPrecision(12, 2);
        builder.Property(l => l.TaxAmount).HasPrecision(12, 2);

        // A dish is never deleted, so a line can always point at the dish it was copied from.
        builder.HasOne<MenuItem>().WithMany().HasForeignKey(l => l.MenuItemId).OnDelete(DeleteBehavior.Restrict);

        // One dish once per order: a repeat is a bigger quantity.
        builder.HasIndex(l => new { l.OrderId, l.MenuItemId }).IsUnique().HasDatabaseName("ux_food_order_lines_dish");
    }
}

public sealed class FoodOrderStatusEntryConfiguration : IEntityTypeConfiguration<FoodOrderStatusEntry>
{
    public void Configure(EntityTypeBuilder<FoodOrderStatusEntry> builder)
    {
        builder.ToTable("food_order_status_history", table =>
        {
            table.HasCheckConstraint(
                "ck_food_order_history_status",
                $"new_status IN ({SnakeCaseEnum<FoodOrderStatus>.SqlList}) AND (previous_status IS NULL OR previous_status IN ({SnakeCaseEnum<FoodOrderStatus>.SqlList}))");
            table.HasCheckConstraint("ck_food_order_history_actor_type", $"changed_by_type IN ({SnakeCaseEnum<FoodOrderActorType>.SqlList})");
            table.HasCheckConstraint(
                "ck_food_order_history_actor",
                $"(changed_by_type = '{SnakeCaseEnum<FoodOrderActorType>.ToDatabase(FoodOrderActorType.System)}') = (changed_by_user_id IS NULL)");
        });

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.PreviousStatus).HasConversion(SnakeCaseEnum<FoodOrderStatus>.Converter).HasMaxLength(32).HasColumnName("previous_status");
        builder.Property(h => h.NewStatus).HasConversion(SnakeCaseEnum<FoodOrderStatus>.Converter).HasMaxLength(32).HasColumnName("new_status");
        builder.Property(h => h.ActorType).HasConversion(SnakeCaseEnum<FoodOrderActorType>.Converter).HasMaxLength(16).HasColumnName("changed_by_type");
        builder.Property(h => h.ActorUserId).HasColumnName("changed_by_user_id");
        builder.Property(h => h.Reason).HasMaxLength(FoodOrder.MaxReasonLength);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(h => h.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(h => new { h.OrderId, h.CreatedAt }).HasDatabaseName("ix_food_order_status_history_order_time");
    }
}
