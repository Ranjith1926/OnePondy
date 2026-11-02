using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Delivery;
using OnePondy.Application.Food;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Food;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Payments;
using OnePondy.UnitTests.Food;

namespace OnePondy.UnitTests.Delivery;

/// <summary>Phase 8 step 8.9: administrators watch every order and delivery, and can cancel or re-offer a stuck one.</summary>
public class FoodOrderAdminTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FoodOrderHarness _h = new();
    private readonly FoodOrderAdminService _admin;
    private readonly DriverService _drivers;
    private readonly DeliveryService _delivery;
    private int _orders;
    private int _phones;

    public FoodOrderAdminTests()
    {
        _admin = new FoodOrderAdminService(_h.Db, _h.Orders, _h.B.Accounts, _h.B.Audit, Options.Create(_h.Food), _h.B.Time);
        _drivers = new DriverService(_h.Db, _h.B.Accounts, _h.B.Audit, _h.B.Time);
        _delivery = new DeliveryService(_h.Db, _h.B.Time, _h.Ledger, Options.Create(_h.Food));
        _h.OpenAsync().GetAwaiter().GetResult();
    }

    private Guid Admin => _h.B.Administrator;

    private async Task<Guid> DriverOnDutyAsync(string name = "Ravi Kumar")
    {
        var id = (await _h.B.Accounts.FindOrCreateAsync(OtpChannel.Sms, $"+9190002{++_phones:00000}", None)).Id;
        Assert.True((await _drivers.SaveMineAsync(id, new DriverInput(name, VehicleType.Scooter, "PY01AB1234", $"PY01202000{_phones:00000}", null), None)).IsSuccess);
        Assert.True((await _drivers.ApproveAsync(id, 0, Admin, None)).IsSuccess);
        Assert.True((await _drivers.SetDutyAsync(id, true, 0, None)).IsSuccess);
        return id;
    }

    /// <summary>An order that was paid and is now confirmed.</summary>
    private async Task<Guid> ConfirmedAsync() => (await _h.PaidAsync($"admin-{++_orders}")).Order.Id;

    private async Task<Guid> PreparingAsync()
    {
        var id = await ConfirmedAsync();
        Assert.True((await _h.Orders.StartPreparingAsync(_h.BusinessId, id, 0, _h.Owner, None)).IsSuccess);
        return id;
    }

    private async Task<Guid> ReadyAsync()
    {
        var id = await PreparingAsync();
        Assert.True((await _h.Orders.MarkReadyAsync(_h.BusinessId, id, 0, _h.Owner, None)).IsSuccess);
        return id;
    }

    private async Task<Guid> JobOfAsync(Guid orderId) => (await _h.Db.DeliveryJobs.AsNoTracking().SingleAsync(j => j.FoodOrderId == orderId)).Id;

    private async Task<DeliveryJob> JobRowAsync(Guid orderId) => await _h.Db.DeliveryJobs.AsNoTracking().SingleAsync(j => j.FoodOrderId == orderId);

    private async Task<AdminOrderSummary> RowAsync(Guid orderId)
    {
        var page = await _admin.ListAsync(new AdminOrderListQuery(null, null, 1, 100), None);
        return page.Items.Single(o => o.Id == orderId);
    }

    private async Task<OrderAttention?> AttentionOfAsync(Guid orderId) => (await RowAsync(orderId)).Attention;

    private void Pass(TimeSpan span) => _h.B.Time.Advance(span);

    // --- The list ---

    [Fact]
    public async Task The_list_shows_every_order_newest_first_with_its_restaurant_and_delivery()
    {
        var first = await ConfirmedAsync();
        Pass(TimeSpan.FromMinutes(1));
        var second = await ReadyAsync();
        var driver = await DriverOnDutyAsync("Meena Sundar");
        await _delivery.AcceptAsync(driver, await JobOfAsync(second), None);

        var page = await _admin.ListAsync(new AdminOrderListQuery(null, null, null, null), None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal([second, first], page.Items.Select(o => o.Id));
        var row = page.Items[0];
        Assert.Equal("Spice Route", row.RestaurantName);
        Assert.Equal(3, row.ItemCount);
        Assert.Equal(FoodOrderHarness.Total, row.TotalAmount);
        Assert.Equal(DeliveryJobStatus.Accepted, row.DeliveryStatus);
        Assert.Equal("Meena", row.DriverFirstName);
        Assert.Null(page.Items[1].DeliveryStatus);
    }

    [Fact]
    public async Task The_list_can_be_filtered_by_status_and_pages()
    {
        var confirmed = await ConfirmedAsync();
        await ReadyAsync();

        var onlyConfirmed = await _admin.ListAsync(new AdminOrderListQuery(FoodOrderStatus.Confirmed, null, null, null), None);
        var firstPage = await _admin.ListAsync(new AdminOrderListQuery(null, null, 1, 1), None);

        Assert.Equal(confirmed, Assert.Single(onlyConfirmed.Items).Id);
        Assert.Single(firstPage.Items);
        Assert.Equal(2, firstPage.TotalCount);
    }

    // --- Needs attention ---

    [Fact]
    public async Task Nothing_needs_attention_while_everything_is_within_its_time()
    {
        var order = await ReadyAsync();
        Pass(TimeSpan.FromMinutes(9));

        Assert.Null(await AttentionOfAsync(order));
        Assert.Equal(0, await _admin.AttentionCountAsync(None));
    }

    [Fact]
    public async Task A_ready_order_nobody_takes_is_flagged_after_ten_minutes()
    {
        var order = await ReadyAsync();
        Pass(TimeSpan.FromMinutes(11));

        Assert.Equal(OrderAttention.NoDriver, await AttentionOfAsync(order));
        Assert.Equal(1, await _admin.AttentionCountAsync(None));
    }

    [Fact]
    public async Task A_driver_who_does_not_collect_is_flagged_after_thirty_minutes_and_a_slow_delivery_after_an_hour()
    {
        var order = await ReadyAsync();
        var driver = await DriverOnDutyAsync();
        var job = await JobOfAsync(order);
        await _delivery.AcceptAsync(driver, job, None);
        Pass(TimeSpan.FromMinutes(29));
        Assert.Null(await AttentionOfAsync(order));

        Pass(TimeSpan.FromMinutes(2));
        Assert.Equal(OrderAttention.PickupOverdue, await AttentionOfAsync(order));

        await _delivery.PickUpAsync(driver, job, None);
        Pass(TimeSpan.FromMinutes(59));
        Assert.Null(await AttentionOfAsync(order));

        Pass(TimeSpan.FromMinutes(2));
        Assert.Equal(OrderAttention.DeliveryOverdue, await AttentionOfAsync(order));
    }

    [Fact]
    public async Task A_kitchen_that_has_not_started_or_has_run_past_its_estimate_is_flagged()
    {
        var waiting = await ConfirmedAsync();
        Pass(TimeSpan.FromMinutes(21));
        Assert.Equal(OrderAttention.KitchenOverdue, await AttentionOfAsync(waiting));

        var cooking = await PreparingAsync();
        // Ready in 25 minutes, and overdue only 20 minutes after that.
        Pass(TimeSpan.FromMinutes(44));
        Assert.Null(await AttentionOfAsync(cooking));
        Pass(TimeSpan.FromMinutes(2));
        Assert.Equal(OrderAttention.KitchenOverdue, await AttentionOfAsync(cooking));
    }

    [Fact]
    public async Task A_refund_still_pending_an_hour_later_is_flagged()
    {
        _h.Payments.RefundsEnabled = true;
        var order = await ConfirmedAsync();
        var cancelled = await _admin.CancelAsync(order, 0, "Out of stock", Admin, None);
        Assert.Equal(FoodOrderStatus.RefundPending, cancelled.Value!.Order.Status);
        Pass(TimeSpan.FromMinutes(59));
        Assert.Null(await AttentionOfAsync(order));

        Pass(TimeSpan.FromMinutes(2));

        Assert.Equal(OrderAttention.RefundOverdue, await AttentionOfAsync(order));
    }

    [Fact]
    public async Task The_attention_filter_lists_only_flagged_orders_oldest_first_and_counts_them()
    {
        var stuck = await ReadyAsync();
        Pass(TimeSpan.FromMinutes(5));
        var fine = await ReadyAsync();
        Pass(TimeSpan.FromMinutes(7));

        var flagged = await _admin.ListAsync(new AdminOrderListQuery(null, true, null, null), None);

        Assert.Equal(stuck, Assert.Single(flagged.Items).Id);
        Assert.Equal(1, flagged.TotalCount);
        Assert.Equal(OrderAttention.NoDriver, flagged.Items[0].Attention);
        Assert.NotEqual(fine, flagged.Items[0].Id);
    }

    [Fact]
    public async Task Orders_that_are_finished_never_need_attention()
    {
        var order = await ReadyAsync();
        var driver = await DriverOnDutyAsync();
        var job = await JobOfAsync(order);
        await _delivery.AcceptAsync(driver, job, None);
        await _delivery.PickUpAsync(driver, job, None);
        await _delivery.DeliverAsync(driver, job, null, None);
        Pass(TimeSpan.FromDays(2));

        Assert.Null(await AttentionOfAsync(order));
        Assert.Equal(0, await _admin.AttentionCountAsync(None));
    }

    // --- One order ---

    [Fact]
    public async Task An_administrator_sees_the_order_the_customers_contact_and_the_driver()
    {
        var order = await ReadyAsync();
        var driver = await DriverOnDutyAsync("Ravi Kumar");
        await _delivery.AcceptAsync(driver, await JobOfAsync(order), None);

        var detail = (await _admin.GetAsync(order, Admin, None)).Value!;

        Assert.Equal("12 Rue Suffren, White Town", detail.Order.DeliveryAddress);
        Assert.Equal("+919876500001", detail.Order.ContactPhone);
        Assert.Equal(FoodOrderHarness.Total, detail.Order.Price.TotalAmount);
        Assert.NotNull(detail.Order.Payment);
        Assert.True(detail.CanCancel);
        var delivery = detail.Delivery!;
        Assert.Equal(DeliveryJobStatus.Accepted, delivery.Status);
        Assert.Equal(driver, delivery.DriverUserId);
        Assert.Equal("Ravi Kumar", delivery.DriverName);
        Assert.StartsWith("+919000", delivery.DriverPhone);
        Assert.Equal(VehicleType.Scooter, delivery.VehicleType);
        Assert.True(delivery.CanRelease);
    }

    [Fact]
    public async Task An_unknown_order_is_not_found()
    {
        Assert.Equal("order_not_found", (await _admin.GetAsync(Guid.NewGuid(), Admin, None)).Error!.Code);
        Assert.Equal("order_not_found", (await _admin.CancelAsync(Guid.NewGuid(), 0, "x", Admin, None)).Error!.Code);
        Assert.Equal("order_not_found", (await _admin.ReleaseDeliveryAsync(Guid.NewGuid(), 0, Admin, None)).Error!.Code);
    }

    [Fact]
    public async Task The_drivers_position_is_shown_only_while_the_delivery_is_under_way_and_every_look_is_recorded()
    {
        var order = await ReadyAsync();
        var driver = await DriverOnDutyAsync();
        var job = await JobOfAsync(order);
        await _delivery.AcceptAsync(driver, job, None);

        // No position reported yet: nothing to show, nothing to record.
        Assert.Null((await _admin.GetAsync(order, Admin, None)).Value!.Delivery!.Position);
        Assert.DoesNotContain(_h.Db.AuditEntries, e => e.Action == AuditActions.DriverLocationViewed);

        await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.93, 79.83), None);
        var seen = (await _admin.GetAsync(order, Admin, None)).Value!.Delivery!.Position!;

        Assert.Equal(11.93, seen.Latitude);
        Assert.Equal(79.83, seen.Longitude);
        var entry = Assert.Single(_h.Db.AuditEntries, e => e.Action == AuditActions.DriverLocationViewed);
        Assert.Equal(Admin, entry.ActorUserId);
        Assert.Equal(order.ToString(), entry.EntityId);

        await _delivery.PickUpAsync(driver, job, None);
        await _delivery.DeliverAsync(driver, job, null, None);
        var after = (await _admin.GetAsync(order, Admin, None)).Value!.Delivery!;

        Assert.Null(after.Position);
        Assert.Equal(1, _h.Db.AuditEntries.Count(e => e.Action == AuditActions.DriverLocationViewed));
    }

    [Fact]
    public async Task The_list_never_carries_the_customers_address_or_a_position()
    {
        var order = await ReadyAsync();
        var driver = await DriverOnDutyAsync();
        var job = await JobOfAsync(order);
        await _delivery.AcceptAsync(driver, job, None);
        await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.93, 79.83), None);

        var json = JsonSerializer.Serialize(await _admin.ListAsync(new AdminOrderListQuery(null, null, null, null), None));

        Assert.DoesNotContain("Suffren", json);
        Assert.DoesNotContain("9876500001", json);
        Assert.DoesNotContain("11.93", json);
    }

    // --- Cancelling ---

    [Fact]
    public async Task Cancelling_needs_a_reason()
    {
        var order = await ReadyAsync();

        var result = await _admin.CancelAsync(order, 0, "  ", Admin, None);

        Assert.Contains("reason", result.Error!.FieldErrors!.Keys);
        Assert.Equal(FoodOrderStatus.ReadyForPickup, (await RowAsync(order)).Status);
    }

    [Fact]
    public async Task Cancelling_a_ready_order_cancels_its_delivery_refunds_the_customer_and_is_recorded()
    {
        _h.Payments.RefundsEnabled = true;
        var order = await ReadyAsync();
        var driver = await DriverOnDutyAsync();
        var job = await JobOfAsync(order);
        await _delivery.AcceptAsync(driver, job, None);
        await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.93, 79.83), None);

        var cancelled = await _admin.CancelAsync(order, 0, " Driver unreachable ", Admin, None);

        var detail = cancelled.Value!;
        Assert.Equal(FoodOrderStatus.RefundPending, detail.Order.Status);
        Assert.Equal("Driver unreachable", detail.Order.ClosedReason);
        Assert.False(detail.CanCancel);
        var row = await JobRowAsync(order);
        Assert.Equal(DeliveryJobStatus.Cancelled, row.Status);
        Assert.Null(row.LastLatitude);
        var refund = Assert.Single(_h.Db.Refunds);
        Assert.Equal(RefundReason.AdminCancelled, refund.Reason);
        Assert.Equal(FoodOrderHarness.Total, refund.Amount);
        var entry = Assert.Single(_h.Db.AuditEntries, e => e.Action == AuditActions.FoodOrderCancelledByAdmin);
        Assert.Equal(Admin, entry.ActorUserId);
        Assert.Contains("Driver unreachable", entry.Details);
        Assert.Contains("ReadyForPickup", entry.Details);
        var step = detail.Order.History.First(h => h.Status == FoodOrderStatus.Cancelled);
        Assert.Equal(FoodOrderActorType.Admin, step.ActorType);
        Assert.Equal(FoodOrderStatus.ReadyForPickup, step.PreviousStatus);
    }

    [Fact]
    public async Task An_order_already_out_for_delivery_can_be_cancelled_when_the_driver_has_vanished()
    {
        _h.Payments.RefundsEnabled = true;
        var order = await ReadyAsync();
        var driver = await DriverOnDutyAsync();
        var job = await JobOfAsync(order);
        await _delivery.AcceptAsync(driver, job, None);
        await _delivery.PickUpAsync(driver, job, None);
        Pass(TimeSpan.FromMinutes(61));
        Assert.Equal(OrderAttention.DeliveryOverdue, await AttentionOfAsync(order));

        var cancelled = await _admin.CancelAsync(order, 0, "Driver never arrived", Admin, None);

        Assert.True(cancelled.IsSuccess, cancelled.Error?.Detail);
        Assert.Equal(DeliveryJobStatus.Cancelled, (await JobRowAsync(order)).Status);
        // The driver is free again.
        Assert.Null((await _delivery.GetMineAsync(driver, None)).Current);
    }

    [Fact]
    public async Task A_paid_order_cannot_be_cancelled_while_refunds_are_off_and_stays_as_it_was()
    {
        _h.Payments.RefundsEnabled = false;
        var order = await ReadyAsync();

        var refused = await _admin.CancelAsync(order, 0, "Out of stock", Admin, None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(FoodOrderStatus.ReadyForPickup, (await RowAsync(order)).Status);
        Assert.Equal(DeliveryJobStatus.Open, (await JobRowAsync(order)).Status);
        Assert.DoesNotContain(_h.Db.AuditEntries, e => e.Action == AuditActions.FoodOrderCancelledByAdmin);
    }

    [Fact]
    public async Task A_delivered_order_cannot_be_cancelled_and_a_stale_page_is_told_it_changed()
    {
        _h.Payments.RefundsEnabled = true;
        var delivered = await ReadyAsync();
        var driver = await DriverOnDutyAsync();
        var job = await JobOfAsync(delivered);
        await _delivery.AcceptAsync(driver, job, None);
        await _delivery.PickUpAsync(driver, job, None);
        await _delivery.DeliverAsync(driver, job, null, None);

        Assert.Equal("order_invalid_transition", (await _admin.CancelAsync(delivered, 0, "Too late", Admin, None)).Error!.Code);

        var other = await ReadyAsync();
        Assert.Equal("order_changed", (await _admin.CancelAsync(other, 999, "Stale", Admin, None)).Error!.Code);
        Assert.Equal(FoodOrderStatus.ReadyForPickup, (await RowAsync(other)).Status);
    }

    // --- Re-offering ---

    [Fact]
    public async Task A_delivery_nobody_collects_can_be_offered_to_the_other_drivers_again()
    {
        var order = await ReadyAsync();
        var slow = await DriverOnDutyAsync("Slow Driver");
        var other = await DriverOnDutyAsync("Meena Sundar");
        var job = await JobOfAsync(order);
        await _delivery.AcceptAsync(slow, job, None);
        await _delivery.RecordLocationAsync(slow, job, new LocationInput(11.93, 79.83), None);

        var released = await _admin.ReleaseDeliveryAsync(order, 0, Admin, None);

        Assert.True(released.IsSuccess, released.Error?.Detail);
        var row = await JobRowAsync(order);
        Assert.Equal(DeliveryJobStatus.Open, row.Status);
        Assert.Null(row.DriverUserId);
        Assert.Null(row.LastLatitude);
        Assert.Equal(DeliveryJobStatus.Open, released.Value!.Delivery!.Status);
        Assert.False(released.Value.Delivery.CanRelease);
        Assert.Equal(slow, JsonSerializer.Deserialize<JsonElement>(Assert.Single(_h.Db.AuditEntries, e => e.Action == AuditActions.DeliveryReleasedByAdmin).Details!).GetProperty("fromDriver").GetGuid());
        // The first driver has nothing now, and another can take it.
        Assert.Null((await _delivery.GetMineAsync(slow, None)).Current);
        Assert.True((await _delivery.AcceptAsync(other, job, None)).IsSuccess);
    }

    [Fact]
    public async Task Only_a_delivery_that_a_driver_holds_but_has_not_collected_can_be_released()
    {
        var open = await ReadyAsync();
        Assert.Equal("delivery_not_releasable", (await _admin.ReleaseDeliveryAsync(open, 0, Admin, None)).Error!.Code);

        var driver = await DriverOnDutyAsync();
        var job = await JobOfAsync(open);
        await _delivery.AcceptAsync(driver, job, None);
        await _delivery.PickUpAsync(driver, job, None);

        Assert.Equal("delivery_not_releasable", (await _admin.ReleaseDeliveryAsync(open, 0, Admin, None)).Error!.Code);
        Assert.Equal(DeliveryJobStatus.PickedUp, (await JobRowAsync(open)).Status);
        Assert.DoesNotContain(_h.Db.AuditEntries, e => e.Action == AuditActions.DeliveryReleasedByAdmin);
    }

    [Fact]
    public async Task Releasing_with_an_out_of_date_view_of_the_delivery_is_refused()
    {
        var order = await ReadyAsync();
        var driver = await DriverOnDutyAsync();
        await _delivery.AcceptAsync(driver, await JobOfAsync(order), None);

        var stale = await _admin.ReleaseDeliveryAsync(order, 999, Admin, None);

        Assert.Equal("delivery_changed", stale.Error!.Code);
        Assert.Equal(DeliveryJobStatus.Accepted, (await JobRowAsync(order)).Status);
    }
}
