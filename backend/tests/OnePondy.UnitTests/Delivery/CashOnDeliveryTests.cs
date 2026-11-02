using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Bookings;
using OnePondy.Application.Delivery;
using OnePondy.Application.Food;
using OnePondy.Application.Payments;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Food;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Payments;
using OnePondy.UnitTests.Food;

namespace OnePondy.UnitTests.Delivery;

/// <summary>Phase 8 step 8.8: paying in cash at the door, the cash a driver then holds, and handing it in.</summary>
public class CashOnDeliveryTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Cash = "cashOnDelivery";

    private readonly FoodOrderHarness _h = new();
    private readonly DriverService _drivers;
    private readonly DeliveryService _delivery;
    private readonly CashService _cash;
    private int _orders;
    private int _phones;

    public CashOnDeliveryTests()
    {
        _drivers = new DriverService(_h.Db, _h.B.Accounts, _h.B.Audit, _h.B.Time);
        _delivery = new DeliveryService(_h.Db, _h.B.Time, _h.Ledger, Options.Create(_h.Food));
        _cash = new CashService(_h.Db, _h.Ledger, _h.B.Audit, _h.B.Accounts, Options.Create(_h.Food), _h.B.Time);
    }

    private Guid Admin => _h.B.Administrator;

    /// <summary>Opens the restaurant, switches the cash on delivery module on, and (by default) has the restaurant opt in.</summary>
    private async Task OpenAsync(bool paymentsOn = true, bool moduleOn = true, bool restaurantOptsIn = true)
    {
        await _h.OpenAsync(paymentsOn);
        if (moduleOn)
        {
            _h.Db.ModuleFlags.Add(new ModuleFlag(ModuleKeys.CashOnDelivery, ModuleStatus.Available));
            await _h.Db.SaveChangesAsync();
        }

        if (restaurantOptsIn)
        {
            await OptInAsync(true);
        }
    }

    private async Task OptInAsync(bool accepts)
    {
        var saved = await _h.Restaurants.SaveMineAsync(_h.BusinessId, _h.Owner, new RestaurantInput(25, 100m, true, ["whiteTown"], 0, accepts), None);
        Assert.True(saved.IsSuccess, saved.Error?.Detail);
    }

    private async Task<Guid> DriverAsync(string name = "Ravi Kumar")
    {
        var id = (await _h.B.Accounts.FindOrCreateAsync(OtpChannel.Sms, $"+9190003{++_phones:00000}", None)).Id;
        Assert.True((await _drivers.SaveMineAsync(id, new DriverInput(name, VehicleType.Scooter, "PY01AB1234", $"PY01203000{_phones:00000}", null), None)).IsSuccess);
        Assert.True((await _drivers.ApproveAsync(id, 0, Admin, None)).IsSuccess);
        Assert.True((await _drivers.SetDutyAsync(id, true, 0, None)).IsSuccess);
        return id;
    }

    /// <summary>A cash order the restaurant has accepted, cooked and marked ready: its delivery job is open.</summary>
    private async Task<(Guid Order, Guid Job)> ReadyCashOrderAsync()
    {
        var placed = await _h.PlacedAsync($"cash-{++_orders}", paymentMethod: Cash);
        Assert.Equal(PaymentMethod.CashOnDelivery, placed.PaymentMethod);
        Assert.True((await _h.Orders.AcceptAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None)).IsSuccess);
        Assert.True((await _h.Orders.StartPreparingAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None)).IsSuccess);
        Assert.True((await _h.Orders.MarkReadyAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None)).IsSuccess);
        var job = await _h.Db.DeliveryJobs.AsNoTracking().SingleAsync(j => j.FoodOrderId == placed.Id);
        return (placed.Id, job.Id);
    }

    /// <summary>Delivers a cash order through the driver's own steps, confirming the exact cash.</summary>
    private async Task DeliveredByAsync(Guid driver, Guid job)
    {
        Assert.True((await _delivery.AcceptAsync(driver, job, None)).IsSuccess);
        Assert.True((await _delivery.PickUpAsync(driver, job, None)).IsSuccess);
        var delivered = await _delivery.DeliverAsync(driver, job, FoodOrderHarness.Total, None);
        Assert.True(delivered.IsSuccess, delivered.Error?.Detail);
    }

    private async Task<QuoteInput> QuoteOfAsync() => new(_h.BusinessId, "whiteTown", [new OrderLineInput(await DosaAsync(), 2)]);

    private async Task<Guid> DosaAsync() => (await _h.Db.MenuItems.AsNoTracking().FirstAsync(i => i.Name == "Masala dosa")).Id;

    private async Task<PaymentOptionsView> OptionsAsync() => (await _h.Orders.QuoteAsync(await QuoteOfAsync(), None)).Value!.PaymentOptions;

    // --- When cash is offered ---

    [Fact]
    public async Task Cash_is_not_offered_until_the_module_is_on_and_the_restaurant_has_opted_in()
    {
        await OpenAsync(moduleOn: false, restaurantOptsIn: false);
        Assert.Equal((true, false), ((await OptionsAsync()).Online, (await OptionsAsync()).CashOnDelivery));

        await OptInAsync(true);
        Assert.False((await OptionsAsync()).CashOnDelivery);

        _h.Db.ModuleFlags.Add(new ModuleFlag(ModuleKeys.CashOnDelivery, ModuleStatus.Available));
        await _h.Db.SaveChangesAsync();
        Assert.True((await OptionsAsync()).CashOnDelivery);

        await OptInAsync(false);
        Assert.False((await OptionsAsync()).CashOnDelivery);
    }

    [Fact]
    public async Task Cash_is_not_offered_for_an_order_over_the_limit()
    {
        await OpenAsync();
        _h.Food.CashOnDelivery.MaxOrderAmount = 300m;

        var options = await OptionsAsync();
        var big = await _h.Orders.QuoteAsync(new QuoteInput(_h.BusinessId, "whiteTown", [new OrderLineInput(await DosaAsync(), 3)]), None);

        Assert.True(options.CashOnDelivery);
        Assert.Equal(300m, options.CashOnDeliveryMaxAmount);
        Assert.False(big.Value!.PaymentOptions.CashOnDelivery);
        Assert.True(big.Value.PaymentOptions.Online);
    }

    [Fact]
    public async Task With_online_payment_unavailable_cash_alone_lets_the_order_through_and_nothing_does_not()
    {
        _h.Food.RequireOnlinePayment = true;
        await OpenAsync(paymentsOn: false);

        var both = (await _h.Orders.QuoteAsync(await QuoteOfAsync(), None)).Value!.PaymentOptions;
        Assert.Equal((false, true), (both.Online, both.CashOnDelivery));

        await OptInAsync(false);
        var neither = await _h.Orders.QuoteAsync(await QuoteOfAsync(), None);
        Assert.Equal("food_payment_unavailable", neither.Error!.Code);
    }

    [Fact]
    public async Task A_customer_cannot_choose_cash_when_it_is_not_offered_or_a_method_that_does_not_exist()
    {
        await OpenAsync(restaurantOptsIn: false);

        var refused = await _h.Orders.PlaceAsync(_h.Buyer, Place("cash-no", Cash), None);
        var nonsense = await _h.Orders.PlaceAsync(_h.Buyer, Place("cash-nonsense", "barter"), None);
        var number = await _h.Orders.PlaceAsync(_h.Buyer, Place("cash-number", "7"), None);

        Assert.Equal("cash_on_delivery_unavailable", refused.Error!.Code);
        Assert.Contains("paymentMethod", nonsense.Error!.FieldErrors!.Keys);
        Assert.Contains("paymentMethod", number.Error!.FieldErrors!.Keys);
        Assert.Empty(_h.Db.FoodOrders);
    }

    [Fact]
    public async Task An_order_without_a_method_is_an_online_order()
    {
        await OpenAsync();

        var placed = await _h.PlacedAsync("default-method");

        Assert.Equal(PaymentMethod.Online, placed.PaymentMethod);
    }

    private PlaceOrderInput Place(string key, string? method) =>
        new(key, _h.BusinessId, "whiteTown", "12 Rue Suffren, White Town", null, "+919876500001", [new OrderLineInput(DosaId(), 2), new OrderLineInput(CoffeeId(), 1)], FoodOrderHarness.Total, method);

    private Guid DosaId() => _h.Db.MenuItems.AsNoTracking().First(i => i.Name == "Masala dosa").Id;

    private Guid CoffeeId() => _h.Db.MenuItems.AsNoTracking().First(i => i.Name == "Filter coffee").Id;

    // --- Accepting and cancelling ---

    [Fact]
    public async Task Accepting_a_cash_order_confirms_it_at_once_even_when_online_payment_is_unavailable()
    {
        _h.Food.RequireOnlinePayment = true;
        await OpenAsync(paymentsOn: false);
        var placed = await _h.PlacedAsync("cash-accept", paymentMethod: Cash);

        var accepted = await _h.Orders.AcceptAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None);

        Assert.Equal(FoodOrderStatus.Confirmed, accepted.Value!.Status);
        Assert.Null(accepted.Value.ExpiresAt);
        Assert.Equal(PaymentMethod.CashOnDelivery, accepted.Value.PaymentMethod);
    }

    [Fact]
    public async Task With_online_payment_available_a_cash_order_is_still_never_sent_for_payment()
    {
        await OpenAsync();
        var online = await _h.PlacedAsync("online-1");
        var cash = await _h.PlacedAsync("cash-1", paymentMethod: Cash);

        Assert.Equal(FoodOrderStatus.AwaitingPayment, (await _h.Orders.AcceptAsync(_h.BusinessId, online.Id, 0, _h.Owner, None)).Value!.Status);
        Assert.Equal(FoodOrderStatus.Confirmed, (await _h.Orders.AcceptAsync(_h.BusinessId, cash.Id, 0, _h.Owner, None)).Value!.Status);
    }

    [Fact]
    public async Task Cancelling_a_cash_order_refunds_nothing_because_nothing_was_paid()
    {
        await OpenAsync();
        _h.Payments.RefundsEnabled = true;
        var placed = await _h.PlacedAsync("cash-cancel", paymentMethod: Cash);
        await _h.Orders.AcceptAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None);

        var cancelled = await _h.Orders.CancelMineAsync(placed.Id, _h.Buyer, "Changed my mind", 0, None);

        Assert.Equal(FoodOrderStatus.Cancelled, cancelled.Value!.Status);
        Assert.Empty(_h.Db.Refunds);
        Assert.Empty(_h.Db.LedgerEntries);
    }

    // --- At the door ---

    [Fact]
    public async Task The_driver_sees_that_it_is_a_cash_order_and_how_much_to_collect_before_and_after_accepting()
    {
        await OpenAsync();
        var (_, job) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();

        var offer = Assert.Single((await _delivery.GetMineAsync(driver, None)).Open);
        var detail = (await _delivery.AcceptAsync(driver, job, None)).Value!;

        Assert.Equal((PaymentMethod.CashOnDelivery, (decimal?)FoodOrderHarness.Total), (offer.PaymentMethod, offer.CashToCollect));
        Assert.Equal((PaymentMethod.CashOnDelivery, (decimal?)FoodOrderHarness.Total), (detail.PaymentMethod, detail.CashToCollect));
    }

    [Fact]
    public async Task An_online_order_shows_no_cash_to_collect()
    {
        await OpenAsync();
        var (order, _, _) = await _h.PaidAsync("online-job");
        await _h.Orders.StartPreparingAsync(_h.BusinessId, order.Id, 0, _h.Owner, None);
        await _h.Orders.MarkReadyAsync(_h.BusinessId, order.Id, 0, _h.Owner, None);
        var driver = await DriverAsync();

        var offer = Assert.Single((await _delivery.GetMineAsync(driver, None)).Open);

        Assert.Equal((PaymentMethod.Online, (decimal?)null), (offer.PaymentMethod, offer.CashToCollect));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(300.0)]
    [InlineData(339.24)]
    [InlineData(400.0)]
    public async Task A_driver_cannot_hand_over_a_cash_order_without_confirming_exactly_its_total(double? confirmed)
    {
        await OpenAsync();
        var (order, job) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();
        await _delivery.AcceptAsync(driver, job, None);
        await _delivery.PickUpAsync(driver, job, None);

        var result = await _delivery.DeliverAsync(driver, job, confirmed is null ? null : (decimal)confirmed, None);

        Assert.Contains("cashCollected", result.Error!.FieldErrors!.Keys);
        var order2 = await _h.Db.FoodOrders.AsNoTracking().SingleAsync(o => o.Id == order);
        Assert.Equal(FoodOrderStatus.OutForDelivery, order2.Status);
        Assert.Null(order2.CashCollected);
        Assert.Empty(_h.Db.LedgerEntries.Where(e => e.Account.StartsWith("driver_cash:")));
    }

    [Fact]
    public async Task An_online_order_cannot_be_handed_over_with_cash_confirmed()
    {
        await OpenAsync();
        var (order, _, _) = await _h.PaidAsync("online-cash");
        await _h.Orders.StartPreparingAsync(_h.BusinessId, order.Id, 0, _h.Owner, None);
        await _h.Orders.MarkReadyAsync(_h.BusinessId, order.Id, 0, _h.Owner, None);
        var driver = await DriverAsync();
        var job = (await _h.Db.DeliveryJobs.AsNoTracking().SingleAsync(j => j.FoodOrderId == order.Id)).Id;
        await _delivery.AcceptAsync(driver, job, None);
        await _delivery.PickUpAsync(driver, job, None);

        var result = await _delivery.DeliverAsync(driver, job, FoodOrderHarness.Total, None);

        Assert.Contains("cashCollected", result.Error!.FieldErrors!.Keys);
        Assert.True((await _delivery.DeliverAsync(driver, job, null, None)).IsSuccess);
    }

    [Fact]
    public async Task Handing_over_a_cash_order_records_the_cash_and_makes_the_driver_owe_it_with_the_usual_split()
    {
        await OpenAsync();
        var (orderId, job) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();

        await DeliveredByAsync(driver, job);

        var order = await _h.Db.FoodOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(FoodOrderStatus.Delivered, order.Status);
        Assert.Equal(FoodOrderHarness.Total, order.CashCollected);
        Assert.Equal(driver, order.CashCollectedBy);
        Assert.Equal(_h.B.Time.GetUtcNow(), order.CashCollectedAt);

        // The same five-way split as an online payment (commission on the dishes, the rest to the restaurant, the fee, the GST), but the debit
        // is the driver's own cash account, and every transaction adds up to zero.
        Assert.Equal(FoodOrderHarness.Total, await _h.BalanceAsync(LedgerAccounts.DriverCash(driver)));
        Assert.Equal(-28.50m, await _h.BalanceAsync(LedgerAccounts.PlatformCommission));
        Assert.Equal(-266.50m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(_h.BusinessId)));
        Assert.Equal(-30m, await _h.BalanceAsync(LedgerAccounts.DeliveryFees));
        Assert.Equal(-14.25m, await _h.BalanceAsync(LedgerAccounts.TaxPayable));
        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.GatewayClearing));
        foreach (var transaction in (await _h.LedgerAsync()).GroupBy(e => e.TransactionId))
        {
            Assert.Equal(0m, transaction.Sum(e => e.Amount));
        }

        var cash = await _cash.GetMineAsync(driver, None);
        Assert.Equal(FoodOrderHarness.Total, cash.Held);
        Assert.Equal(order.CashCollectedAt, cash.LastCollectedAt);
        Assert.Null(cash.LastDepositAt);
    }

    [Fact]
    public async Task Handing_over_twice_posts_the_cash_once()
    {
        await OpenAsync();
        var (_, job) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();
        await DeliveredByAsync(driver, job);

        var again = await _delivery.DeliverAsync(driver, job, FoodOrderHarness.Total, None);

        Assert.Equal("job_invalid_transition", again.Error!.Code);
        Assert.Equal(FoodOrderHarness.Total, await _h.BalanceAsync(LedgerAccounts.DriverCash(driver)));
    }

    // --- The limit on what a driver holds ---

    [Fact]
    public async Task A_driver_holding_as_much_cash_as_allowed_is_not_given_another_cash_order_until_they_hand_some_in()
    {
        await OpenAsync();
        _h.Food.CashOnDelivery.MaxDriverCashHeld = 500m;
        var (_, first) = await ReadyCashOrderAsync();
        var (_, second) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();
        await DeliveredByAsync(driver, first);

        var refused = await _delivery.AcceptAsync(driver, second, None);

        Assert.Equal("driver_cash_limit", refused.Error!.Code);
        Assert.Equal(DeliveryJobStatus.Open, (await _h.Db.DeliveryJobs.AsNoTracking().SingleAsync(j => j.Id == second)).Status);

        Assert.True((await _cash.RecordDepositAsync(driver, new DepositInput(200m, null, null, null), Admin, None)).IsSuccess);
        Assert.True((await _delivery.AcceptAsync(driver, second, None)).IsSuccess);
    }

    [Fact]
    public async Task The_cash_limit_does_not_stop_a_driver_taking_an_online_order()
    {
        await OpenAsync();
        _h.Food.CashOnDelivery.MaxDriverCashHeld = 340m;
        var (_, first) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();
        await DeliveredByAsync(driver, first);
        var (online, _, _) = await _h.PaidAsync("online-after-cash");
        await _h.Orders.StartPreparingAsync(_h.BusinessId, online.Id, 0, _h.Owner, None);
        await _h.Orders.MarkReadyAsync(_h.BusinessId, online.Id, 0, _h.Owner, None);
        var onlineJob = (await _h.Db.DeliveryJobs.AsNoTracking().SingleAsync(j => j.FoodOrderId == online.Id)).Id;

        Assert.True((await _delivery.AcceptAsync(driver, onlineJob, None)).IsSuccess);
    }

    // --- Handing the cash in ---

    [Fact]
    public async Task An_administrator_records_cash_handed_in_and_the_drivers_balance_goes_down_by_exactly_that()
    {
        await OpenAsync();
        var (_, job) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();
        await DeliveredByAsync(driver, job);
        _h.B.Time.Advance(TimeSpan.FromHours(5));
        var handedInAt = _h.B.Time.GetUtcNow().AddHours(-1);

        var recorded = await _cash.RecordDepositAsync(driver, new DepositInput(139.25m, handedInAt, " REC-17 ", " Part of today's cash "), Admin, None);

        Assert.True(recorded.IsSuccess, recorded.Error?.Detail);
        Assert.Equal(200m, recorded.Value!.Held);
        Assert.Equal(handedInAt, recorded.Value.LastDepositAt);
        Assert.Equal(200m, await _h.BalanceAsync(LedgerAccounts.DriverCash(driver)));
        Assert.Equal(139.25m, await _h.BalanceAsync(LedgerAccounts.CashReceived));
        var deposit = Assert.Single(_h.Db.DriverCashDeposits);
        Assert.Equal(("REC-17", "Part of today's cash", Admin), (deposit.Reference, deposit.Note, deposit.RecordedByUserId));
        foreach (var transaction in (await _h.LedgerAsync()).GroupBy(e => e.TransactionId))
        {
            Assert.Equal(0m, transaction.Sum(e => e.Amount));
        }

        var entry = Assert.Single(_h.Db.AuditEntries, e => e.Action == AuditActions.DriverCashDepositRecorded);
        Assert.Equal((Admin, driver.ToString()), (entry.ActorUserId, entry.EntityId));
        Assert.Equal(139.25m, JsonSerializer.Deserialize<JsonElement>(entry.Details!).GetProperty("amount").GetDecimal());

        Assert.True((await _cash.RecordDepositAsync(driver, new DepositInput(200m, null, null, null), Admin, None)).IsSuccess);
        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.DriverCash(driver)));
    }

    [Fact]
    public async Task A_deposit_cannot_be_more_than_the_driver_holds_or_otherwise_make_no_sense()
    {
        await OpenAsync();
        var (_, job) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();
        await DeliveredByAsync(driver, job);

        foreach (var amount in new decimal?[] { null, 0m, -5m, 339.26m, 100.123m, 2_000_000m })
        {
            var result = await _cash.RecordDepositAsync(driver, new DepositInput(amount, null, null, null), Admin, None);
            Assert.Contains("amount", result.Error!.FieldErrors!.Keys);
        }

        var future = await _cash.RecordDepositAsync(driver, new DepositInput(10m, _h.B.Time.GetUtcNow().AddMinutes(5), null, null), Admin, None);
        var tooLong = await _cash.RecordDepositAsync(driver, new DepositInput(10m, null, new string('r', 101), null), Admin, None);

        Assert.Contains("receivedAt", future.Error!.FieldErrors!.Keys);
        Assert.Contains("reference", tooLong.Error!.FieldErrors!.Keys);
        Assert.Empty(_h.Db.DriverCashDeposits);
        Assert.Equal(FoodOrderHarness.Total, await _h.BalanceAsync(LedgerAccounts.DriverCash(driver)));
    }

    [Fact]
    public async Task A_deposit_for_someone_who_is_not_a_driver_is_refused()
    {
        await OpenAsync();

        var result = await _cash.RecordDepositAsync(Guid.NewGuid(), new DepositInput(10m, null, null, null), Admin, None);

        Assert.Equal("driver_not_found", result.Error!.Code);
    }

    [Fact]
    public async Task The_administrators_list_shows_who_holds_cash_and_hides_those_who_have_handed_it_all_in()
    {
        await OpenAsync();
        var (_, first) = await ReadyCashOrderAsync();
        var (_, second) = await ReadyCashOrderAsync();
        var holder = await DriverAsync("Ravi Kumar");
        var settled = await DriverAsync("Meena Sundar");
        await DeliveredByAsync(holder, first);
        await DeliveredByAsync(settled, second);
        await _cash.RecordDepositAsync(settled, new DepositInput(FoodOrderHarness.Total, null, null, null), Admin, None);

        var holding = await _cash.AdminListAsync(onlyHolding: true, None);
        var everyone = await _cash.AdminListAsync(onlyHolding: false, None);

        var row = Assert.Single(holding);
        Assert.Equal((holder, "Ravi Kumar", FoodOrderHarness.Total), (row.DriverUserId, row.DriverName, row.Held));
        Assert.StartsWith("+919000", row.PhoneNumber);
        Assert.Null(row.LastDepositAt);
        Assert.NotNull(row.LastCollectedAt);
        Assert.Equal(2, everyone.Count);
        Assert.Equal(0m, everyone.Single(r => r.DriverUserId == settled).Held);
        Assert.NotNull(everyone.Single(r => r.DriverUserId == settled).LastDepositAt);
    }

    // --- Statements and leaving ---

    [Fact]
    public async Task A_delivered_cash_order_goes_on_the_restaurants_statement_with_the_same_amounts_as_an_online_one()
    {
        await OpenAsync();
        var (orderId, job) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();
        await DeliveredByAsync(driver, job);
        var today = DateOnly.FromDateTime(_h.B.Time.GetUtcNow().UtcDateTime);

        var draft = await _h.Settlements.DraftAsync(_h.BusinessId, today.AddDays(-6), today, Admin, None);

        Assert.True(draft.IsSuccess, draft.Error?.Detail);
        var statement = draft.Value!;
        Assert.Equal((295m, 28.50m, 0m, 266.50m), (statement.Settlement.GrossAmount, statement.Settlement.CommissionAmount, statement.Settlement.RefundAmount, statement.Settlement.NetAmount));
        var item = Assert.Single(statement.Items);
        Assert.Equal((orderId, SettlementItemKind.Sale, 266.50m), (item.FoodOrderId!.Value, item.Kind, item.PartnerAmount));
        Assert.Equal(-await _h.BalanceAsync(LedgerAccounts.PartnerPayable(_h.BusinessId)), statement.Settlement.NetAmount);

        // The restaurant is owed even though the driver has not yet handed the cash in: that is OnePondy's matter with the driver.
        Assert.Equal(FoodOrderHarness.Total, await _h.BalanceAsync(LedgerAccounts.DriverCash(driver)));
    }

    [Fact]
    public async Task A_cash_order_that_was_cancelled_or_not_yet_delivered_is_not_on_a_statement()
    {
        await OpenAsync();
        var (_, job) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();
        await _delivery.AcceptAsync(driver, job, None);
        await _delivery.PickUpAsync(driver, job, None);
        var today = DateOnly.FromDateTime(_h.B.Time.GetUtcNow().UtcDateTime);

        var draft = await _h.Settlements.DraftAsync(_h.BusinessId, today.AddDays(-1), today, Admin, None);

        Assert.Equal("settlement_nothing_due", draft.Error!.Code);
    }

    [Fact]
    public async Task A_driver_holding_cash_cannot_delete_their_account_until_they_hand_it_in()
    {
        await OpenAsync();
        var (_, job) = await ReadyCashOrderAsync();
        var driver = await DriverAsync();
        await DeliveredByAsync(driver, job);
        var guard = new BookingsDeletionGuard(_h.Db);

        Assert.Contains(BookingsDeletionGuard.DriverHoldsCash, await guard.GetBlockersAsync(driver, None));

        await _cash.RecordDepositAsync(driver, new DepositInput(FoodOrderHarness.Total, null, null, null), Admin, None);

        Assert.DoesNotContain(BookingsDeletionGuard.DriverHoldsCash, await guard.GetBlockersAsync(driver, None));
    }

    // --- The restaurant's switch ---

    [Fact]
    public async Task A_restaurant_keeps_its_cash_setting_when_it_saves_without_mentioning_it()
    {
        await OpenAsync();

        var saved = await _h.Restaurants.SaveMineAsync(_h.BusinessId, _h.Owner, new RestaurantInput(30, 100m, true, ["whiteTown"], 0), None);

        Assert.True(saved.Value!.AcceptsCashOnDelivery);
        Assert.Equal(30, saved.Value.PrepTimeMinutes);

        var off = await _h.Restaurants.SaveMineAsync(_h.BusinessId, _h.Owner, new RestaurantInput(30, 100m, true, ["whiteTown"], 0, false), None);
        Assert.False(off.Value!.AcceptsCashOnDelivery);
    }

    [Fact]
    public async Task A_new_restaurant_does_not_take_cash_until_it_says_so()
    {
        await _h.OpenAsync();

        var profile = (await _h.Restaurants.GetMineAsync(_h.BusinessId, _h.Owner, None)).Value!;

        Assert.False(profile.AcceptsCashOnDelivery);
    }
}
