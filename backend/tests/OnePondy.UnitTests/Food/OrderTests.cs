using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Food;
using OnePondy.Application.Identity;
using OnePondy.Application.Payments;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Food;
using OnePondy.Domain.Identity;
using OnePondy.UnitTests.Bookings;
using OnePondy.UnitTests.Businesses;
using OnePondy.UnitTests.Identity;

namespace OnePondy.UnitTests.Food;

/// <summary>Phase 8 step 8.2: pricing, order placement and the order state machine, on the in-memory database with a controllable clock.</summary>
public class OrderTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Address = "12 Rue Suffren, White Town";
    private const string Phone = "+919876500001";

    private readonly BusinessHarness _b = new();
    private readonly RestaurantService _restaurants;
    private readonly MenuService _menu;
    private readonly OrderService _orders;
    private readonly FoodOrderExpiry _expiry;
    private readonly FoodOptions _options = new();
    private readonly Guid _owner;
    private readonly Guid _buyer;
    private readonly Guid _otherBuyer;

    public OrderTests()
    {
        var audit = new AuditTrail(_b.Db, _b.Time);
        _restaurants = new RestaurantService(_b.Db, audit, _b.Time);
        _menu = new MenuService(_b.Db, _restaurants, audit, _b.Time);
        var payments = FoodOrderHarness.PaymentServices(_b.Db, _b.Time, new PaymentsOptions(), new FakePaymentGateway());
        _orders = new OrderService(_b.Db, _restaurants, payments.Refunds, Options.Create(_options), _b.Time);
        _expiry = new FoodOrderExpiry(_b.Db, _orders, payments.Payments, _b.Time, Options.Create(_options));
        _owner = _b.Customer;
        _buyer = _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000022222", None).Result.Id;
        _otherBuyer = _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000033333", None).Result.Id;
    }

    // --- Setup ---

    private sealed record Shop(Guid BusinessId, Guid Category, Guid Dosa, Guid Coffee);

    /// <summary>An open restaurant on White Town with a dosa (₹120) and a coffee (₹45), both at the default 5% GST, smallest order ₹100.</summary>
    private async Task<Shop> OpenShopAsync(string name = "Spice Route", bool foodModuleOn = true, params string[] areas)
    {
        if (foodModuleOn)
        {
            _b.Db.ModuleFlags.Add(new ModuleFlag(ModuleKeys.Food, ModuleStatus.Available));
            await _b.Db.SaveChangesAsync();
        }

        var applied = await _b.Businesses.ApplyAsync(_owner, BusinessHarness.Input(name, OnePondy.Domain.Businesses.BusinessType.Restaurant, "12345678901234"), None);
        var business = (await _b.Businesses.ApproveAsync(applied.Value!.Id, applied.Value.Version, _b.Administrator, None)).Value!;
        var category = (await _menu.CreateCategoryAsync(business.Id, _owner, new MenuCategoryInput("Breakfast", null, null), None)).Value!.Id;
        var dosa = (await _menu.CreateItemAsync(business.Id, _owner, new MenuItemInput(category, "Masala dosa", null, 120m, true, null, null), None)).Value!;
        var coffee = (await _menu.CreateItemAsync(business.Id, _owner, new MenuItemInput(category, "Filter coffee", null, 45m, true, null, null), None)).Value!;
        var saved = await _restaurants.SaveMineAsync(
            business.Id, _owner, new RestaurantInput(25, 100m, true, areas.Length == 0 ? ["whiteTown"] : areas, null), None);
        Assert.True(saved.IsSuccess, saved.Error?.Detail);
        return new Shop(business.Id, category, dosa.Id, coffee.Id);
    }

    private static OrderLineInput Line(Guid itemId, int quantity = 1) => new(itemId, quantity);

    private static PlaceOrderInput Place(
        Shop shop,
        decimal? expectedTotal,
        string key = "order-1",
        string area = "whiteTown",
        params OrderLineInput[] lines) =>
        new(key, shop.BusinessId, area, Address, "Ring the bell", Phone, lines.Length == 0 ? [Line(shop.Dosa, 2), Line(shop.Coffee)] : lines, expectedTotal);

    /// <summary>Two dosas and a coffee: 285 of food, 14.25 GST, 40 of fees.</summary>
    private const decimal DefaultTotal = 339.25m;

    private async Task<OrderDetail> PlacedAsync(Shop shop, string key = "order-1", Guid? customer = null)
    {
        var placed = await _orders.PlaceAsync(customer ?? _buyer, Place(shop, DefaultTotal, key), None);
        Assert.True(placed.IsSuccess, placed.Error?.Detail);
        return placed.Value!;
    }

    private async Task EnablePaymentsAsync()
    {
        _b.Db.ModuleFlags.Add(new ModuleFlag(ModuleKeys.Payments, ModuleStatus.Available));
        await _b.Db.SaveChangesAsync();
    }

    // --- Pricing ---

    [Fact]
    public async Task A_quote_is_priced_from_the_menu_with_tax_per_line_and_flat_fees()
    {
        var shop = await OpenShopAsync();

        var quote = (await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", [Line(shop.Dosa, 2), Line(shop.Coffee)]), None)).Value!;

        Assert.Equal(285m, quote.Price.Subtotal);
        Assert.Equal(14.25m, quote.Price.TaxAmount);
        Assert.Equal(30m, quote.Price.DeliveryFee);
        Assert.Equal(10m, quote.Price.PackagingFee);
        Assert.Equal(0m, quote.Price.DiscountAmount);
        Assert.Equal(DefaultTotal, quote.Price.TotalAmount);
        Assert.Equal([12m, 2.25m], quote.Lines.Select(l => l.TaxAmount));
        Assert.Equal([240m, 45m], quote.Lines.Select(l => l.Subtotal));
        Assert.Equal(25, quote.PrepTimeMinutes);
    }

    [Fact]
    public void Tax_is_rounded_per_line_and_the_order_tax_is_the_sum_of_the_lines()
    {
        var item = MenuItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), new MenuItemContent("Idli", null, 33.33m, true, 5m), 0, DateTimeOffset.UnixEpoch);

        var line = FoodPricing.Line(item, 3);
        var price = FoodPricing.Total([line, line], 30m, 10m);

        Assert.Equal(99.99m, line.Subtotal);
        Assert.Equal(5.00m, line.TaxAmount); // 4.9995 rounds up, away from zero
        Assert.Equal(10.00m, price.TaxAmount);
        Assert.Equal(199.98m + 10.00m + 40m, price.TotalAmount);
    }

    [Fact]
    public async Task The_same_dish_listed_twice_is_one_line_with_the_quantities_added()
    {
        var shop = await OpenShopAsync();

        var quote = (await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", [Line(shop.Dosa), Line(shop.Coffee), Line(shop.Dosa)]), None)).Value!;

        Assert.Equal(2, quote.Lines.Count);
        Assert.Equal(2, quote.Lines[0].Quantity);
        Assert.Equal("Masala dosa", quote.Lines[0].Name);
    }

    [Fact]
    public async Task A_cart_that_breaks_the_rules_is_refused_with_the_reason()
    {
        var shop = await OpenShopAsync();
        var areaError = (await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "puducherry", [Line(shop.Dosa, 2)]), None)).Error!;
        var unknownArea = (await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "mars", [Line(shop.Dosa, 2)]), None)).Error!;
        var noLines = (await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", []), None)).Error!;
        var zeroQuantity = (await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", [Line(shop.Dosa, 0)]), None)).Error!;
        var tooMany = (await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", [Line(shop.Dosa, 21)]), None)).Error!;
        var unknownDish = (await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", [Line(Guid.NewGuid())]), None)).Error!;
        var tooSmall = (await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", [Line(shop.Coffee, 2)]), None)).Error!;

        Assert.Contains("area", areaError.FieldErrors!.Keys);
        Assert.Contains("area", unknownArea.FieldErrors!.Keys);
        Assert.Contains("lines", noLines.FieldErrors!.Keys);
        Assert.Contains("lines", zeroQuantity.FieldErrors!.Keys);
        Assert.Contains("lines", tooMany.FieldErrors!.Keys);
        Assert.Contains("lines", unknownDish.FieldErrors!.Keys);
        Assert.Equal("order_below_minimum", tooSmall.Code);
    }

    [Fact]
    public async Task A_dish_from_another_restaurant_is_not_accepted()
    {
        var shop = await OpenShopAsync("Spice Route");
        var other = await OpenShopAsync("Beach Cafe", foodModuleOn: false);

        var result = await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", [Line(other.Dosa, 2)]), None);

        Assert.Contains("lines", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Dishes_that_are_sold_out_or_removed_are_refused_by_name()
    {
        var shop = await OpenShopAsync();
        await _menu.SetAvailabilityAsync(shop.BusinessId, shop.Coffee, 0, false, _owner, None);

        var result = await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", [Line(shop.Dosa, 2), Line(shop.Coffee)]), None);

        Assert.Equal("order_item_unavailable", result.Error!.Code);
        Assert.Contains("Filter coffee", result.Error.Detail);
        Assert.DoesNotContain("Masala dosa", result.Error.Detail);
    }

    [Fact]
    public async Task A_closed_restaurant_or_one_that_does_not_deliver_to_the_area_cannot_be_ordered_from()
    {
        var shop = await OpenShopAsync();

        var elsewhere = await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "auroville", [Line(shop.Dosa, 2)]), None);
        var current = (await _restaurants.GetMineAsync(shop.BusinessId, _owner, None)).Value!;
        await _restaurants.SaveMineAsync(shop.BusinessId, _owner, new RestaurantInput(25, 100m, false, ["whiteTown"], current.Version), None);
        var closed = await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", [Line(shop.Dosa, 2)]), None);

        Assert.Equal("restaurant_area_not_served", elsewhere.Error!.Code);
        Assert.Equal("restaurant_not_taking_orders", closed.Error!.Code);
    }

    [Fact]
    public async Task A_restaurant_that_delivers_to_the_whole_city_serves_every_area()
    {
        var shop = await OpenShopAsync("Spice Route", true, "puducherry");

        var result = await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "auroville", [Line(shop.Dosa, 2)]), None);

        Assert.True(result.IsSuccess);
        Assert.Equal("auroville", result.Value!.Area);
    }

    // --- Placing an order ---

    [Fact]
    public async Task Placing_an_order_saves_the_price_and_waits_for_the_restaurant()
    {
        var shop = await OpenShopAsync();

        var order = await PlacedAsync(shop);

        Assert.Equal(FoodOrderStatus.Placed, order.Status);
        Assert.Equal(_b.Time.GetUtcNow().AddMinutes(5), order.ExpiresAt);
        Assert.Equal(DefaultTotal, order.Price.TotalAmount);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal("whiteTown", order.DeliveryArea);
        Assert.Equal(Address, order.DeliveryAddress);
        Assert.Equal(Phone, order.ContactPhone);
        Assert.Equal("12345678901234", order.FssaiLicenseNumber);
        Assert.True(order.CanCancel);
        Assert.Single(order.History);
        Assert.Equal(FoodOrderActorType.Customer, order.History[0].ActorType);
    }

    [Fact]
    public async Task An_order_is_refused_while_the_food_module_is_off()
    {
        var shop = await OpenShopAsync(foodModuleOn: false);

        var result = await _orders.PlaceAsync(_buyer, Place(shop, DefaultTotal), None);

        Assert.Equal("food_not_available", result.Error!.Code);
        Assert.Empty(await _b.Db.FoodOrders.ToListAsync());
    }

    [Fact]
    public async Task The_same_attempt_sent_twice_is_one_order_and_a_new_key_is_a_new_order()
    {
        var shop = await OpenShopAsync();

        var first = await PlacedAsync(shop, "retry-key");
        var second = await PlacedAsync(shop, "retry-key");
        var third = await PlacedAsync(shop, "another-key");

        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.Id, third.Id);
        Assert.Equal(2, await _b.Db.FoodOrders.CountAsync());
    }

    [Fact]
    public async Task Two_customers_may_use_the_same_key_without_seeing_each_others_order()
    {
        var shop = await OpenShopAsync();

        var mine = await PlacedAsync(shop, "same-key", _buyer);
        var theirs = await PlacedAsync(shop, "same-key", _otherBuyer);

        Assert.NotEqual(mine.Id, theirs.Id);
    }

    [Fact]
    public async Task A_price_that_changed_since_the_customer_saw_it_is_refused_with_the_new_total()
    {
        var shop = await OpenShopAsync();
        var dosa = (await _menu.GetMineAsync(shop.BusinessId, _owner, false, None)).Value!.Categories[0].Items.Single(i => i.Id == shop.Dosa);
        await _menu.UpdateItemAsync(shop.BusinessId, shop.Dosa, dosa.Version, _owner, new MenuItemInput(shop.Category, "Masala dosa", null, 130m, true, null, null), None);

        var result = await _orders.PlaceAsync(_buyer, Place(shop, DefaultTotal), None);

        Assert.Equal("order_price_changed", result.Error!.Code);
        Assert.Contains("360.25", result.Error.Detail); // 2 x 130 + 45 = 305, GST 15.25, fees 40
        Assert.Empty(await _b.Db.FoodOrders.ToListAsync());
    }

    [Fact]
    public async Task Placing_an_order_needs_the_total_the_customer_was_shown()
    {
        var shop = await OpenShopAsync();

        var result = await _orders.PlaceAsync(_buyer, Place(shop, null), None);

        Assert.Contains("expectedTotal", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Delivery_details_are_checked_and_the_phone_is_tidied()
    {
        var shop = await OpenShopAsync();
        var bad = await _orders.PlaceAsync(_buyer, Place(shop, DefaultTotal) with { Address = " ", ContactPhone = "call me" }, None);
        var tidy = await _orders.PlaceAsync(_buyer, Place(shop, DefaultTotal, "tidy") with { ContactPhone = "+91 98765-00002" }, None);

        Assert.Contains("address", bad.Error!.FieldErrors!.Keys);
        Assert.Contains("contactPhone", bad.Error.FieldErrors.Keys);
        Assert.Equal("+919876500002", tidy.Value!.ContactPhone);
    }

    [Fact]
    public async Task A_menu_edit_after_ordering_does_not_change_the_order()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);
        var dosa = (await _menu.GetMineAsync(shop.BusinessId, _owner, false, None)).Value!.Categories[0].Items.Single(i => i.Id == shop.Dosa);

        await _menu.UpdateItemAsync(shop.BusinessId, shop.Dosa, dosa.Version, _owner, new MenuItemInput(shop.Category, "Plain dosa", null, 999m, true, 18m, null), None);
        var again = (await _orders.GetMineAsync(order.Id, _buyer, None)).Value!;

        // Still the dish as it was ordered (not "Plain dosa", not ₹999, not 18% GST).
        var dosaLine = Assert.Single(again.Lines, l => l.Name == "Masala dosa");
        Assert.Equal(120m, dosaLine.UnitPrice);
        Assert.Equal(5m, dosaLine.TaxRatePercent);
        Assert.DoesNotContain(again.Lines, l => l.Name == "Plain dosa");
        Assert.Equal(DefaultTotal, again.Price.TotalAmount);
    }

    [Fact]
    public async Task Only_the_customer_who_placed_an_order_can_see_or_cancel_it()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);

        Assert.Equal("order_not_found", (await _orders.GetMineAsync(order.Id, _otherBuyer, None)).Error!.Code);
        Assert.Equal("order_not_found", (await _orders.CancelMineAsync(order.Id, _otherBuyer, null, order.Version, None)).Error!.Code);
        Assert.Empty(await _orders.ListMineAsync(_otherBuyer, None));
        Assert.Single(await _orders.ListMineAsync(_buyer, None));
    }

    [Fact]
    public async Task The_customers_list_shows_the_restaurant_and_the_number_of_items()
    {
        var shop = await OpenShopAsync();
        await PlacedAsync(shop);

        var summary = Assert.Single(await _orders.ListMineAsync(_buyer, None));

        Assert.Equal("Spice Route", summary.RestaurantName);
        Assert.Equal(3, summary.ItemCount);
        Assert.Equal(DefaultTotal, summary.TotalAmount);
    }

    // --- The restaurant's side ---

    [Fact]
    public async Task Only_the_owner_sees_or_works_an_order_and_the_restaurant_never_sees_the_address_or_phone()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);

        Assert.Equal("order_not_found", (await _orders.ListForBusinessAsync(shop.BusinessId, _buyer, null, None)).Error!.Code);
        Assert.Equal("order_not_found", (await _orders.GetForBusinessAsync(shop.BusinessId, order.Id, _buyer, None)).Error!.Code);
        Assert.Equal("order_not_found", (await _orders.AcceptAsync(shop.BusinessId, order.Id, order.Version, _buyer, None)).Error!.Code);

        var detail = (await _orders.GetForBusinessAsync(shop.BusinessId, order.Id, _owner, None)).Value!;
        var json = JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("Suffren", json);
        Assert.DoesNotContain("9876500001", json);
        Assert.Equal("whiteTown", detail.DeliveryArea);
        var inbox = await _orders.ListForBusinessAsync(shop.BusinessId, _owner, null, None);
        var row = Assert.Single(inbox.Value!);
        Assert.Equal(order.Id, row.Id);
        Assert.DoesNotContain("Suffren", JsonSerializer.Serialize(row));
        Assert.DoesNotContain("9876500001", JsonSerializer.Serialize(row));
    }

    [Fact]
    public async Task An_inbox_row_shows_the_dishes_to_cook_and_the_version_an_action_must_send()
    {
        var shop = await OpenShopAsync();
        await PlacedAsync(shop);

        var row = Assert.Single((await _orders.ListForBusinessAsync(shop.BusinessId, _owner, null, None)).Value!);

        // By name, so a kitchen ticket reads the same every time it is loaded.
        Assert.Equal(["Filter coffee", "Masala dosa"], row.Lines.Select(l => l.Name));
        Assert.Equal([1, 2], row.Lines.Select(l => l.Quantity));
        Assert.Equal(3, row.ItemCount);
        Assert.Equal(DefaultTotal, row.TotalAmount);

        // The version on the row is the one the order is at, so acting with it works (and a made-up one does not).
        var stale = await _orders.AcceptAsync(shop.BusinessId, row.Id, row.Version + 1, _owner, None);
        Assert.Equal("order_changed", stale.Error!.Code);
        var accepted = await _orders.AcceptAsync(shop.BusinessId, row.Id, row.Version, _owner, None);
        Assert.Equal(FoodOrderStatus.Confirmed, accepted.Value!.Status);
    }

    [Fact]
    public async Task The_inbox_shows_when_cooking_should_finish_once_it_has_started()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);
        var confirmed = (await _orders.AcceptAsync(shop.BusinessId, order.Id, order.Version, _owner, None)).Value!;
        await _orders.StartPreparingAsync(shop.BusinessId, order.Id, confirmed.Version, _owner, None);

        var row = Assert.Single((await _orders.ListForBusinessAsync(shop.BusinessId, _owner, FoodOrderStatus.Preparing, None)).Value!);

        Assert.Equal(_b.Time.GetUtcNow().AddMinutes(25), row.EstimatedReadyAt);
    }

    [Fact]
    public async Task Accepting_confirms_the_order_straight_away_while_online_payment_is_off()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);

        var accepted = (await _orders.AcceptAsync(shop.BusinessId, order.Id, order.Version, _owner, None)).Value!;

        Assert.Equal(FoodOrderStatus.Confirmed, accepted.Status);
        Assert.Null(accepted.ExpiresAt);
    }

    [Fact]
    public async Task Accepting_with_online_payment_on_gives_the_customer_a_fresh_window_to_pay()
    {
        var shop = await OpenShopAsync();
        await EnablePaymentsAsync();
        var order = await PlacedAsync(shop);
        _b.Time.Advance(TimeSpan.FromMinutes(3));

        var accepted = (await _orders.AcceptAsync(shop.BusinessId, order.Id, order.Version, _owner, None)).Value!;

        Assert.Equal(FoodOrderStatus.AwaitingPayment, accepted.Status);
        Assert.Equal(_b.Time.GetUtcNow().AddMinutes(10), accepted.ExpiresAt);
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_and_the_customer_can_read_it()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);

        var blank = await _orders.RejectAsync(shop.BusinessId, order.Id, order.Version, _owner, " ", None);
        var rejected = await _orders.RejectAsync(shop.BusinessId, order.Id, order.Version, _owner, "Out of batter", None);
        var seen = (await _orders.GetMineAsync(order.Id, _buyer, None)).Value!;

        Assert.Contains("reason", blank.Error!.FieldErrors!.Keys);
        Assert.Equal(FoodOrderStatus.Rejected, rejected.Value!.Status);
        Assert.Equal("Out of batter", seen.ClosedReason);
        Assert.False(seen.CanCancel);
    }

    [Fact]
    public async Task The_kitchen_moves_an_order_through_preparing_to_ready_and_the_estimate_follows_the_prep_time()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);
        await _orders.AcceptAsync(shop.BusinessId, order.Id, order.Version, _owner, None);

        var preparing = (await _orders.StartPreparingAsync(shop.BusinessId, order.Id, 0, _owner, None)).Value!;
        var ready = (await _orders.MarkReadyAsync(shop.BusinessId, order.Id, 0, _owner, None)).Value!;

        Assert.Equal(FoodOrderStatus.Preparing, preparing.Status);
        Assert.Equal(_b.Time.GetUtcNow().AddMinutes(25), preparing.EstimatedReadyAt);
        Assert.Equal(FoodOrderStatus.ReadyForPickup, ready.Status);
        Assert.Equal(
            [FoodOrderStatus.Placed, FoodOrderStatus.Confirmed, FoodOrderStatus.Preparing, FoodOrderStatus.ReadyForPickup],
            ready.History.Select(h => h.Status));
    }

    [Fact]
    public async Task The_kitchen_cannot_skip_a_step()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);

        var early = await _orders.StartPreparingAsync(shop.BusinessId, order.Id, order.Version, _owner, None);
        var ready = await _orders.MarkReadyAsync(shop.BusinessId, order.Id, order.Version, _owner, None);

        Assert.Equal("order_invalid_transition", early.Error!.Code);
        Assert.Equal("order_invalid_transition", ready.Error!.Code);
        Assert.Equal(FoodOrderStatus.Placed, (await _orders.GetMineAsync(order.Id, _buyer, None)).Value!.Status);
    }

    [Fact]
    public async Task A_stale_version_is_refused()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);

        var result = await _orders.AcceptAsync(shop.BusinessId, order.Id, order.Version + 5, _owner, None);

        Assert.Equal("order_changed", result.Error!.Code);
    }

    [Fact]
    public async Task A_save_that_loses_a_race_is_reported_as_a_change_and_leaves_the_order_alone()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);
        _b.Db.FailNextSaveWith = new DbUpdateConcurrencyException("someone else got there first");

        var result = await _orders.AcceptAsync(shop.BusinessId, order.Id, order.Version, _owner, None);

        Assert.Equal("order_changed", result.Error!.Code);
        Assert.Equal(FoodOrderStatus.Placed, (await _orders.GetMineAsync(order.Id, _buyer, None)).Value!.Status);
    }

    // --- Cancelling ---

    [Fact]
    public async Task The_customer_can_cancel_until_the_restaurant_starts_preparing()
    {
        var shop = await OpenShopAsync();
        await EnablePaymentsAsync();
        var waiting = await PlacedAsync(shop, "waiting");
        var awaiting = await PlacedAsync(shop, "awaiting");
        await _orders.AcceptAsync(shop.BusinessId, awaiting.Id, awaiting.Version, _owner, None);

        var fromPlaced = await _orders.CancelMineAsync(waiting.Id, _buyer, "Changed my mind", waiting.Version, None);
        var fromAwaitingPayment = await _orders.CancelMineAsync(awaiting.Id, _buyer, null, awaiting.Version, None);

        Assert.Equal(FoodOrderStatus.Cancelled, fromPlaced.Value!.Status);
        Assert.Equal("Changed my mind", fromPlaced.Value.ClosedReason);
        Assert.Equal(FoodOrderStatus.Cancelled, fromAwaitingPayment.Value!.Status);
        Assert.Null(fromPlaced.Value.ExpiresAt);
    }

    [Fact]
    public async Task The_customer_cannot_cancel_once_preparing_has_started()
    {
        var shop = await OpenShopAsync();
        var confirmed = await PlacedAsync(shop, "confirmed");
        await _orders.AcceptAsync(shop.BusinessId, confirmed.Id, 0, _owner, None);
        var stillOk = await _orders.CancelMineAsync(confirmed.Id, _buyer, null, 0, None);
        Assert.True(stillOk.IsSuccess); // a confirmed order is still cancellable

        var second = await PlacedAsync(shop, "second");
        await _orders.AcceptAsync(shop.BusinessId, second.Id, 0, _owner, None);
        await _orders.StartPreparingAsync(shop.BusinessId, second.Id, 0, _owner, None);
        var late = await _orders.CancelMineAsync(second.Id, _buyer, null, 0, None);
        var seen = (await _orders.GetMineAsync(second.Id, _buyer, None)).Value!;

        Assert.Equal("order_invalid_transition", late.Error!.Code);
        Assert.Contains("support", late.Error.Detail);
        Assert.Equal(FoodOrderStatus.Preparing, seen.Status);
        Assert.False(seen.CanCancel);
    }

    [Fact]
    public async Task The_restaurant_can_cancel_a_confirmed_or_cooking_order_but_a_waiting_one_is_rejected_instead()
    {
        var shop = await OpenShopAsync();
        var waiting = await PlacedAsync(shop, "waiting");
        var cooking = await PlacedAsync(shop, "cooking");
        await _orders.AcceptAsync(shop.BusinessId, cooking.Id, 0, _owner, None);
        await _orders.StartPreparingAsync(shop.BusinessId, cooking.Id, 0, _owner, None);

        var fromWaiting = await _orders.CancelForBusinessAsync(shop.BusinessId, waiting.Id, 0, _owner, "Closing early", None);
        var withoutReason = await _orders.CancelForBusinessAsync(shop.BusinessId, cooking.Id, 0, _owner, null, None);
        var fromCooking = await _orders.CancelForBusinessAsync(shop.BusinessId, cooking.Id, 0, _owner, "Gas ran out", None);

        Assert.Equal("order_invalid_transition", fromWaiting.Error!.Code);
        Assert.Contains("reason", withoutReason.Error!.FieldErrors!.Keys);
        Assert.Equal(FoodOrderStatus.Cancelled, fromCooking.Value!.Status);
        Assert.Equal(FoodOrderActorType.Partner, fromCooking.Value.History[^1].ActorType);
    }

    // --- Timeouts ---

    [Fact]
    public async Task An_order_the_restaurant_never_answered_expires()
    {
        var shop = await OpenShopAsync();
        var order = await PlacedAsync(shop);
        _b.Time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, (await _expiry.ExpireDueAsync(None)).Expired);

        _b.Time.Advance(TimeSpan.FromMinutes(2));
        var expired = (await _expiry.ExpireDueAsync(None)).Expired;
        var seen = (await _orders.GetMineAsync(order.Id, _buyer, None)).Value!;

        Assert.Equal(1, expired);
        Assert.Equal(FoodOrderStatus.Expired, seen.Status);
        Assert.Equal(FoodOrderActorType.System, seen.History[^1].ActorType);
        Assert.Null(seen.ExpiresAt);
    }

    [Fact]
    public async Task An_order_nobody_paid_for_expires_but_a_confirmed_one_does_not()
    {
        var shop = await OpenShopAsync();
        await EnablePaymentsAsync();
        var unpaid = await PlacedAsync(shop, "unpaid");
        await _orders.AcceptAsync(shop.BusinessId, unpaid.Id, 0, _owner, None);
        _b.Db.ModuleFlags.RemoveRange(_b.Db.ModuleFlags.Where(f => f.Key == ModuleKeys.Payments));
        await _b.Db.SaveChangesAsync();
        var confirmed = await PlacedAsync(shop, "confirmed");
        await _orders.AcceptAsync(shop.BusinessId, confirmed.Id, 0, _owner, None);

        _b.Time.Advance(TimeSpan.FromMinutes(30));
        var expired = (await _expiry.ExpireDueAsync(None)).Expired;

        Assert.Equal(1, expired);
        Assert.Equal(FoodOrderStatus.Expired, (await _orders.GetMineAsync(unpaid.Id, _buyer, None)).Value!.Status);
        Assert.Equal(FoodOrderStatus.Confirmed, (await _orders.GetMineAsync(confirmed.Id, _buyer, None)).Value!.Status);
    }

    // --- The state table, row by row ---

    private static FoodOrder NewOrder()
    {
        var item = MenuItem.Create(Guid.NewGuid(), Guid.NewGuid(), new MenuItemContent("Dosa", null, 120m, true, 5m), 0, DateTimeOffset.UnixEpoch);
        var price = FoodPricing.Total([FoodPricing.Line(item, 1)], 30m, 10m);
        return FoodOrder.Place(
            Guid.NewGuid(), Guid.NewGuid(), "k", new DeliveryDetails("whiteTown", "addr", null, Phone), 20, price, DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5));
    }

    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddHours(1);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private static FoodOrder OrderIn(FoodOrderStatus status)
    {
        var order = NewOrder();
        switch (status)
        {
            case FoodOrderStatus.Placed:
                break;
            case FoodOrderStatus.AwaitingPayment:
                order.Accept(Staff, Now, true, Window);
                break;
            case FoodOrderStatus.Confirmed:
                order.Accept(Staff, Now, false, Window);
                break;
            case FoodOrderStatus.Preparing:
                order.Accept(Staff, Now, false, Window);
                order.StartPreparing(Staff, Now);
                break;
            case FoodOrderStatus.ReadyForPickup:
                order.Accept(Staff, Now, false, Window);
                order.StartPreparing(Staff, Now);
                order.MarkReady(Staff, Now);
                break;
            case FoodOrderStatus.OutForDelivery:
                order.Accept(Staff, Now, false, Window);
                order.StartPreparing(Staff, Now);
                order.MarkReady(Staff, Now);
                order.PickUp(Staff, Now);
                break;
            case FoodOrderStatus.Delivered:
                order.Accept(Staff, Now, false, Window);
                order.StartPreparing(Staff, Now);
                order.MarkReady(Staff, Now);
                order.PickUp(Staff, Now);
                order.Deliver(Staff, Now);
                break;
            case FoodOrderStatus.Rejected:
                order.Reject(Staff, "no", Now);
                break;
            case FoodOrderStatus.Cancelled:
                order.Cancel(FoodOrderActorType.Customer, Staff, null, Now);
                break;
            case FoodOrderStatus.Expired:
                order.Expire(Now);
                break;
            case FoodOrderStatus.Failed:
                order.Accept(Staff, Now, true, Window);
                order.Fail("declined", Now);
                break;
            case FoodOrderStatus.RefundPending:
                order.Cancel(FoodOrderActorType.Customer, Staff, null, Now);
                order.RequestRefund(FoodOrderActorType.System, null, Now);
                break;
            case FoodOrderStatus.Refunded:
                order.Cancel(FoodOrderActorType.Customer, Staff, null, Now);
                order.RequestRefund(FoodOrderActorType.System, null, Now);
                order.MarkRefunded(Now);
                break;
        }

        Assert.Equal(status, order.Status);
        return order;
    }

    /// <summary>Every action an order can be asked to do, and the statuses it is allowed from. Anything not listed must be refused.</summary>
    private static readonly (string Name, Action<FoodOrder> Do, FoodOrderStatus[] AllowedFrom)[] Table =
    [
        ("accept", o => o.Accept(Staff, Now, true, Window), [FoodOrderStatus.Placed]),
        ("confirm payment", o => o.ConfirmPayment(Now), [FoodOrderStatus.AwaitingPayment]),
        ("reject", o => o.Reject(Staff, "no", Now), [FoodOrderStatus.Placed, FoodOrderStatus.AwaitingPayment]),
        ("start preparing", o => o.StartPreparing(Staff, Now), [FoodOrderStatus.Confirmed]),
        ("mark ready", o => o.MarkReady(Staff, Now), [FoodOrderStatus.Preparing]),
        ("pick up", o => o.PickUp(Staff, Now), [FoodOrderStatus.ReadyForPickup]),
        ("deliver", o => o.Deliver(Staff, Now), [FoodOrderStatus.OutForDelivery]),
        ("customer cancels", o => o.Cancel(FoodOrderActorType.Customer, Staff, null, Now), [FoodOrderStatus.Placed, FoodOrderStatus.AwaitingPayment, FoodOrderStatus.Confirmed]),
        ("restaurant cancels", o => o.Cancel(FoodOrderActorType.Partner, Staff, "x", Now), [FoodOrderStatus.Confirmed, FoodOrderStatus.Preparing, FoodOrderStatus.ReadyForPickup]),
        ("admin cancels", o => o.Cancel(FoodOrderActorType.Admin, Staff, "x", Now), [FoodOrderStatus.Placed, FoodOrderStatus.AwaitingPayment, FoodOrderStatus.Confirmed, FoodOrderStatus.Preparing, FoodOrderStatus.ReadyForPickup, FoodOrderStatus.OutForDelivery]),
        ("system cancels", o => o.Cancel(FoodOrderActorType.System, null, "x", Now), []),
        ("expire", o => o.Expire(Now), [FoodOrderStatus.Placed, FoodOrderStatus.AwaitingPayment]),
        ("fail", o => o.Fail("declined", Now), [FoodOrderStatus.AwaitingPayment]),
        ("request refund", o => o.RequestRefund(FoodOrderActorType.System, null, Now), [FoodOrderStatus.Rejected, FoodOrderStatus.Cancelled]),
        ("mark refunded", o => o.MarkRefunded(Now), [FoodOrderStatus.RefundPending]),
    ];

    [Fact]
    public void Every_action_is_allowed_from_exactly_the_statuses_in_the_table_and_refused_from_all_others()
    {
        foreach (var status in Enum.GetValues<FoodOrderStatus>())
        {
            foreach (var (name, act, allowedFrom) in Table)
            {
                var order = OrderIn(status);
                var historyBefore = order.History.Count;

                if (allowedFrom.Contains(status))
                {
                    act(order);
                    Assert.True(order.History.Count > historyBefore, $"'{name}' from {status} should be recorded");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => act(order));
                    Assert.Equal(status, order.Status);
                    Assert.Equal(historyBefore, order.History.Count);
                }
            }
        }
    }

    [Fact]
    public void Only_the_waiting_states_have_a_deadline()
    {
        foreach (var status in Enum.GetValues<FoodOrderStatus>())
        {
            var order = OrderIn(status);

            Assert.Equal(status is FoodOrderStatus.Placed or FoodOrderStatus.AwaitingPayment, order.ExpiresAt is not null);
            Assert.Equal(order.IsHolding, order.ExpiresAt is not null);
        }
    }

    [Fact]
    public void Only_placing_an_order_starts_with_a_customer_step_and_every_step_is_recorded_once_in_order()
    {
        var order = OrderIn(FoodOrderStatus.Delivered);

        Assert.Equal(
            [FoodOrderStatus.Placed, FoodOrderStatus.Confirmed, FoodOrderStatus.Preparing, FoodOrderStatus.ReadyForPickup, FoodOrderStatus.OutForDelivery, FoodOrderStatus.Delivered],
            order.History.Select(h => h.NewStatus));
        Assert.Null(order.History[0].PreviousStatus);
        Assert.All(order.History.Skip(1), h => Assert.NotNull(h.PreviousStatus));
    }

    [Fact]
    public void An_order_needs_dishes_and_a_sensible_key()
    {
        var item = MenuItem.Create(Guid.NewGuid(), Guid.NewGuid(), new MenuItemContent("Dosa", null, 120m, true, 5m), 0, DateTimeOffset.UnixEpoch);
        var empty = FoodPricing.Total([], 30m, 10m);
        var price = FoodPricing.Total([FoodPricing.Line(item, 1)], 30m, 10m);
        var delivery = new DeliveryDetails("whiteTown", "addr", null, Phone);

        Assert.Throws<ArgumentException>(() => FoodOrder.Place(Guid.NewGuid(), Guid.NewGuid(), "k", delivery, 20, empty, Now, Window));
        Assert.Throws<ArgumentException>(() => FoodOrder.Place(Guid.NewGuid(), Guid.NewGuid(), " ", delivery, 20, price, Now, Window));
        Assert.Throws<ArgumentOutOfRangeException>(() => FoodOrder.Place(Guid.NewGuid(), Guid.NewGuid(), new string('k', 65), delivery, 20, price, Now, Window));
    }

    // --- Settings ---

    [Fact]
    public void Food_settings_that_would_give_nonsense_are_refused_at_start_up()
    {
        FoodOptionsGuard.EnsureSafe(new FoodOptions());

        Assert.Throws<InvalidOperationException>(() => FoodOptionsGuard.EnsureSafe(new FoodOptions { AcceptWindow = TimeSpan.Zero }));
        Assert.Throws<InvalidOperationException>(() => FoodOptionsGuard.EnsureSafe(new FoodOptions { PaymentWindow = TimeSpan.FromMinutes(1) }));
        Assert.Throws<InvalidOperationException>(() => FoodOptionsGuard.EnsureSafe(new FoodOptions { DeliveryFee = -1m }));
        Assert.Throws<InvalidOperationException>(() => FoodOptionsGuard.EnsureSafe(new FoodOptions { PackagingFee = 501m }));
    }

    [Fact]
    public async Task Fees_come_from_the_settings()
    {
        var shop = await OpenShopAsync();
        _options.DeliveryFee = 50m;
        _options.PackagingFee = 0m;

        var quote = (await _orders.QuoteAsync(new QuoteInput(shop.BusinessId, "whiteTown", [Line(shop.Dosa, 2), Line(shop.Coffee)]), None)).Value!;

        Assert.Equal(50m, quote.Price.DeliveryFee);
        Assert.Equal(0m, quote.Price.PackagingFee);
        Assert.Equal(285m + 14.25m + 50m, quote.Price.TotalAmount);
    }
}
