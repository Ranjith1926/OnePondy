using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Delivery;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Food;
using OnePondy.Domain.Identity;
using OnePondy.UnitTests.Food;

namespace OnePondy.UnitTests.Delivery;

/// <summary>Phase 8 step 8.7: a ready order is put up for drivers, one driver takes it, collects it and hands it over.</summary>
public class DeliveryTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Address = "12 Rue Suffren, White Town";

    private readonly FoodOrderHarness _h = new();
    private readonly DriverService _drivers;
    private readonly DeliveryService _delivery;
    private int _orders;
    private int _phones;

    public DeliveryTests()
    {
        _drivers = new DriverService(_h.Db, _h.B.Accounts, _h.B.Audit, _h.B.Time);
        _delivery = new DeliveryService(_h.Db, _h.B.Time, _h.Ledger, Options.Create(_h.Food));
        _h.OpenAsync(paymentsOn: false).GetAwaiter().GetResult();
    }

    /// <summary>An approved driver on a bicycle, already on duty.</summary>
    private async Task<Guid> DriverOnDutyAsync(string name = "Ravi Kumar")
    {
        var id = (await _h.B.Accounts.FindOrCreateAsync(OtpChannel.Sms, $"+9190001{++_phones:00000}", None)).Id;
        Assert.True((await _drivers.SaveMineAsync(id, new DriverInput(name, VehicleType.Bicycle, null, null, null), None)).IsSuccess);
        Assert.True((await _drivers.ApproveAsync(id, 0, _h.B.Administrator, None)).IsSuccess);
        Assert.True((await _drivers.SetDutyAsync(id, true, 0, None)).IsSuccess);
        return id;
    }

    /// <summary>An order the restaurant has marked ready, so a delivery job is open.</summary>
    private async Task<Guid> ReadyOrderAsync()
    {
        var placed = await _h.PlacedAsync($"order-{++_orders}");
        Assert.True((await _h.Orders.AcceptAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None)).IsSuccess);
        Assert.True((await _h.Orders.StartPreparingAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None)).IsSuccess);
        Assert.True((await _h.Orders.MarkReadyAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None)).IsSuccess);
        return placed.Id;
    }

    private async Task<DeliveryJob> JobOfAsync(Guid orderId) => await _h.Db.DeliveryJobs.AsNoTracking().SingleAsync(j => j.FoodOrderId == orderId);

    private async Task<FoodOrderStatus> StatusOfAsync(Guid orderId) => (await _h.Db.FoodOrders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status;

    private async Task<Guid> TakenAsync(Guid driver, Guid orderId)
    {
        var job = await JobOfAsync(orderId);
        var accepted = await _delivery.AcceptAsync(driver, job.Id, None);
        Assert.True(accepted.IsSuccess, accepted.Error?.Detail);
        return job.Id;
    }

    // --- Opening ---

    [Fact]
    public async Task Marking_an_order_ready_opens_exactly_one_delivery_job()
    {
        var order = await ReadyOrderAsync();

        var job = await JobOfAsync(order);

        Assert.Equal(DeliveryJobStatus.Open, job.Status);
        Assert.Null(job.DriverUserId);
        Assert.Equal(1, await _h.Db.DeliveryJobs.CountAsync());
    }

    [Fact]
    public async Task An_order_that_is_not_ready_has_no_job()
    {
        var placed = await _h.PlacedAsync();
        await _h.Orders.AcceptAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None);
        await _h.Orders.StartPreparingAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None);

        Assert.Empty(await _h.Db.DeliveryJobs.ToListAsync());
    }

    [Fact]
    public async Task The_customer_and_the_restaurant_see_that_the_order_is_waiting_for_a_driver()
    {
        var order = await ReadyOrderAsync();

        var mine = (await _h.Orders.GetMineAsync(order, _h.Buyer, None)).Value!;
        var inbox = (await _h.Orders.ListForBusinessAsync(_h.BusinessId, _h.Owner, null, None)).Value!.Single();

        Assert.Equal(DeliveryJobStatus.Open, mine.Delivery!.Status);
        Assert.Null(mine.Delivery.DriverFirstName);
        Assert.Equal(DeliveryJobStatus.Open, inbox.Delivery!.Status);
    }

    // --- Offers ---

    [Fact]
    public async Task A_driver_on_duty_sees_open_jobs_without_the_customers_address_or_phone()
    {
        await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();

        var view = await _delivery.GetMineAsync(driver, None);

        var offer = Assert.Single(view.Open);
        Assert.True(view.CanAcceptJobs);
        Assert.Null(view.Current);
        Assert.Equal("Spice Route", offer.RestaurantName);
        Assert.Equal("whiteTown", offer.DeliveryArea);
        Assert.Equal(3, offer.ItemCount);
        var json = JsonSerializer.Serialize(view);
        Assert.DoesNotContain(Address, json);
        Assert.DoesNotContain("+919876500001", json);
    }

    [Fact]
    public async Task A_driver_off_duty_or_not_approved_sees_nothing_and_cannot_accept()
    {
        var order = await ReadyOrderAsync();
        var job = await JobOfAsync(order);
        var off = await DriverOnDutyAsync();
        var current = await _drivers.GetMineAsync(off, None);
        await _drivers.SetDutyAsync(off, false, current.Version, None);
        var applicant = (await _h.B.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000777771", None)).Id;
        await _drivers.SaveMineAsync(applicant, new DriverInput("New Driver", VehicleType.Bicycle, null, null, null), None);

        foreach (var driver in new[] { off, applicant, Guid.NewGuid() })
        {
            var view = await _delivery.GetMineAsync(driver, None);
            Assert.False(view.CanAcceptJobs);
            Assert.Empty(view.Open);
            Assert.Equal("driver_not_available", (await _delivery.AcceptAsync(driver, job.Id, None)).Error!.Code);
        }

        Assert.Equal(DeliveryJobStatus.Open, (await JobOfAsync(order)).Status);
    }

    [Fact]
    public async Task Open_jobs_are_listed_oldest_first_and_only_while_open()
    {
        var first = await ReadyOrderAsync();
        _h.B.Time.Advance(TimeSpan.FromMinutes(5));
        var second = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var other = await DriverOnDutyAsync("Meena S");

        Assert.Equal(2, (await _delivery.GetMineAsync(driver, None)).Open.Count);
        await TakenAsync(other, first);

        var remaining = (await _delivery.GetMineAsync(driver, None)).Open;
        Assert.Equal((await JobOfAsync(second)).Id, Assert.Single(remaining).Id);
    }

    // --- Accepting ---

    [Fact]
    public async Task Accepting_gives_the_driver_the_job_and_the_customers_details()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await JobOfAsync(order);

        var accepted = (await _delivery.AcceptAsync(driver, job.Id, None)).Value!;

        Assert.Equal(DeliveryJobStatus.Accepted, accepted.Status);
        Assert.Equal(Address, accepted.DeliveryAddress);
        Assert.Equal("+919876500001", accepted.ContactPhone);
        Assert.Equal("Spice Route", accepted.RestaurantName);
        Assert.Contains(accepted.Items, i => i is { Name: "Masala dosa", Quantity: 2 });
        Assert.Equal(driver, (await JobOfAsync(order)).DriverUserId);
        Assert.Equal(FoodOrderStatus.ReadyForPickup, await StatusOfAsync(order));
        var view = await _delivery.GetMineAsync(driver, None);
        Assert.Equal(job.Id, view.Current!.Id);
        Assert.Empty(view.Open);
    }

    [Fact]
    public async Task A_job_someone_else_has_already_taken_is_refused()
    {
        var order = await ReadyOrderAsync();
        var first = await DriverOnDutyAsync();
        var second = await DriverOnDutyAsync("Meena S");
        var job = await TakenAsync(first, order);

        var late = await _delivery.AcceptAsync(second, job, None);

        Assert.Equal("job_taken", late.Error!.Code);
        Assert.Equal(first, (await JobOfAsync(order)).DriverUserId);
    }

    [Fact]
    public async Task Accepting_twice_by_the_same_driver_is_not_an_error()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);

        var again = await _delivery.AcceptAsync(driver, job, None);

        Assert.True(again.IsSuccess);
        Assert.Equal(DeliveryJobStatus.Accepted, again.Value!.Status);
    }

    [Fact]
    public async Task A_driver_holds_one_delivery_at_a_time()
    {
        var first = await ReadyOrderAsync();
        var second = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        await TakenAsync(driver, first);

        var another = await _delivery.AcceptAsync(driver, (await JobOfAsync(second)).Id, None);

        Assert.Equal("driver_has_active_job", another.Error!.Code);
        Assert.Equal(DeliveryJobStatus.Open, (await JobOfAsync(second)).Status);
    }

    [Fact]
    public async Task An_unknown_job_is_not_found()
    {
        var driver = await DriverOnDutyAsync();

        Assert.Equal("job_not_found", (await _delivery.AcceptAsync(driver, Guid.NewGuid(), None)).Error!.Code);
    }

    // --- Collecting and handing over ---

    [Fact]
    public async Task Picking_up_sends_the_order_out_and_names_the_driver_to_the_customer()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync("Ravi Kumar");
        var job = await TakenAsync(driver, order);

        var picked = (await _delivery.PickUpAsync(driver, job, None)).Value!;

        Assert.Equal(DeliveryJobStatus.PickedUp, picked.Status);
        Assert.Equal(FoodOrderStatus.OutForDelivery, await StatusOfAsync(order));
        var mine = (await _h.Orders.GetMineAsync(order, _h.Buyer, None)).Value!;
        Assert.Equal(DeliveryJobStatus.PickedUp, mine.Delivery!.Status);
        Assert.Equal("Ravi", mine.Delivery.DriverFirstName);
        Assert.Equal(VehicleType.Bicycle, mine.Delivery.VehicleType);
        var step = mine.History.Last();
        Assert.Equal(FoodOrderStatus.OutForDelivery, step.Status);
        Assert.Equal(FoodOrderActorType.Driver, step.ActorType);
    }

    [Fact]
    public async Task Only_the_driver_who_has_the_job_can_collect_or_deliver_it()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var other = await DriverOnDutyAsync("Meena S");
        var job = await TakenAsync(driver, order);

        Assert.Equal("job_not_found", (await _delivery.PickUpAsync(other, job, None)).Error!.Code);
        Assert.Equal("job_not_found", (await _delivery.DeliverAsync(other, job, null, None)).Error!.Code);
        Assert.Equal("job_not_found", (await _delivery.ReleaseAsync(other, job, None)).Error!.Code);
        Assert.Equal(FoodOrderStatus.ReadyForPickup, await StatusOfAsync(order));
    }

    [Fact]
    public async Task Delivering_finishes_the_order_and_frees_the_driver_for_the_next_one()
    {
        var order = await ReadyOrderAsync();
        var next = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);
        await _delivery.PickUpAsync(driver, job, None);

        var delivered = (await _delivery.DeliverAsync(driver, job, null, None)).Value!;

        Assert.Equal(DeliveryJobStatus.Delivered, delivered.Status);
        Assert.Equal(FoodOrderStatus.Delivered, await StatusOfAsync(order));
        var finished = await JobOfAsync(order);
        Assert.Equal(driver, finished.DriverUserId);
        Assert.NotNull(finished.DeliveredAt);
        var view = await _delivery.GetMineAsync(driver, None);
        Assert.Null(view.Current);
        Assert.Equal((await JobOfAsync(next)).Id, Assert.Single(view.Open).Id);
        Assert.True((await _delivery.AcceptAsync(driver, (await JobOfAsync(next)).Id, None)).IsSuccess);
    }

    [Fact]
    public async Task The_steps_must_come_in_order()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);

        Assert.Equal("job_invalid_transition", (await _delivery.DeliverAsync(driver, job, null, None)).Error!.Code);
        Assert.Equal(FoodOrderStatus.ReadyForPickup, await StatusOfAsync(order));

        await _delivery.PickUpAsync(driver, job, None);

        Assert.Equal("job_invalid_transition", (await _delivery.PickUpAsync(driver, job, None)).Error!.Code);
        Assert.Equal("job_invalid_transition", (await _delivery.ReleaseAsync(driver, job, None)).Error!.Code);
        Assert.Equal(FoodOrderStatus.OutForDelivery, await StatusOfAsync(order));
    }

    // --- Giving it up ---

    [Fact]
    public async Task A_driver_can_give_the_job_up_before_collecting_and_another_can_take_it()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var other = await DriverOnDutyAsync("Meena S");
        var job = await TakenAsync(driver, order);

        var released = await _delivery.ReleaseAsync(driver, job, None);

        Assert.True(released.IsSuccess);
        var reopened = await JobOfAsync(order);
        Assert.Equal(DeliveryJobStatus.Open, reopened.Status);
        Assert.Null(reopened.DriverUserId);
        Assert.Null(reopened.AcceptedAt);
        Assert.True((await _delivery.AcceptAsync(other, job, None)).IsSuccess);
        Assert.Equal(other, (await JobOfAsync(order)).DriverUserId);
    }

    // --- Location ---

    [Fact]
    public async Task The_drivers_position_is_kept_only_while_they_hold_the_job_and_only_the_latest()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);

        Assert.True((await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.93, 79.83), None)).IsSuccess);
        _h.B.Time.Advance(TimeSpan.FromSeconds(20));
        Assert.True((await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.94, 79.84), None)).IsSuccess);

        var held = await JobOfAsync(order);
        Assert.Equal(11.94, held.LastLatitude);
        Assert.Equal(79.84, held.LastLongitude);
        Assert.Equal(_h.B.Time.GetUtcNow(), held.LastLocationAt);
        var mine = (await _h.Orders.GetMineAsync(order, _h.Buyer, None)).Value!;
        Assert.Equal(held.LastLocationAt, mine.Delivery!.LocationUpdatedAt);

        await _delivery.PickUpAsync(driver, job, None);
        await _delivery.DeliverAsync(driver, job, null, None);

        var finished = await JobOfAsync(order);
        Assert.Null(finished.LastLatitude);
        Assert.Null(finished.LastLongitude);
        Assert.Null(finished.LastLocationAt);
        Assert.Equal("job_invalid_transition", (await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.9, 79.8), None)).Error!.Code);
    }

    [Fact]
    public async Task The_customer_sees_where_the_driver_is_only_while_the_driver_is_carrying_the_food()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);
        await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.93, 79.83), None);

        // On the way to the restaurant: the customer is told who is coming, but not where they are.
        var beforePickup = (await _h.Orders.GetMineAsync(order, _h.Buyer, None)).Value!;
        Assert.Null(beforePickup.Delivery!.Position);

        await _delivery.PickUpAsync(driver, job, None);
        await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.94, 79.84), None);

        var carrying = (await _h.Orders.GetMineAsync(order, _h.Buyer, None)).Value!;
        Assert.Equal(new DeliveryPosition(11.94, 79.84), carrying.Delivery!.Position);
        Assert.Equal(_h.B.Time.GetUtcNow(), carrying.Delivery.LocationUpdatedAt);

        await _delivery.DeliverAsync(driver, job, null, None);

        var delivered = (await _h.Orders.GetMineAsync(order, _h.Buyer, None)).Value!;
        Assert.Null(delivered.Delivery!.Position);
    }

    [Fact]
    public async Task The_restaurant_and_staff_never_get_the_drivers_position_through_the_order()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);
        await _delivery.PickUpAsync(driver, job, None);
        await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.94, 79.84), None);

        var customer = (await _h.Orders.GetMineAsync(order, _h.Buyer, None)).Value!;
        var inbox = (await _h.Orders.ListForBusinessAsync(_h.BusinessId, _h.Owner, null, None)).Value!.Single();
        var restaurant = (await _h.Orders.GetForBusinessAsync(_h.BusinessId, order, _h.Owner, None)).Value!;
        var admin = (await _h.Orders.GetForAdminAsync(order, None)).Value!;

        Assert.NotNull(customer.Delivery!.Position);
        Assert.Null(inbox.Delivery!.Position);
        Assert.Null(restaurant.Delivery!.Position);
        Assert.Null(admin.Delivery!.Position);
    }

    [Fact]
    public async Task Giving_the_job_up_erases_the_position()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);
        await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.93, 79.83), None);

        await _delivery.ReleaseAsync(driver, job, None);

        var reopened = await JobOfAsync(order);
        Assert.Null(reopened.LastLatitude);
        Assert.Null(reopened.LastLocationAt);
    }

    [Theory]
    [InlineData(91, 10)]
    [InlineData(-91, 10)]
    [InlineData(10, 181)]
    [InlineData(10, -181)]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.PositiveInfinity)]
    public async Task A_position_that_is_not_on_earth_is_refused(double latitude, double longitude)
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);

        var result = await _delivery.RecordLocationAsync(driver, job, new LocationInput(latitude, longitude), None);

        Assert.Equal("validation", ErrorKindOf(result));
        Assert.Null((await JobOfAsync(order)).LastLatitude);
    }

    [Fact]
    public async Task A_missing_coordinate_is_refused()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);

        var result = await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.9, null), None);

        Assert.Contains("longitude", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Nobody_can_report_a_position_for_someone_elses_job_or_for_a_job_they_do_not_hold()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var other = await DriverOnDutyAsync("Meena S");
        var job = await JobOfAsync(order);

        // An open job has nobody holding it, so nobody can report for it.
        Assert.Equal("job_not_found", (await _delivery.RecordLocationAsync(driver, job.Id, new LocationInput(11.9, 79.8), None)).Error!.Code);
        await _delivery.AcceptAsync(driver, job.Id, None);
        Assert.Equal("job_not_found", (await _delivery.RecordLocationAsync(other, job.Id, new LocationInput(11.9, 79.8), None)).Error!.Code);
        Assert.Null((await JobOfAsync(order)).LastLatitude);
    }

    // --- Cancelling ---

    [Fact]
    public async Task Cancelling_the_order_cancels_an_open_delivery()
    {
        var order = await ReadyOrderAsync();

        var cancelled = await _h.Orders.CancelForBusinessAsync(_h.BusinessId, order, 0, _h.Owner, "Out of gas", None);

        Assert.True(cancelled.IsSuccess, cancelled.Error?.Detail);
        var job = await JobOfAsync(order);
        Assert.Equal(DeliveryJobStatus.Cancelled, job.Status);
        Assert.NotNull(job.CancelledAt);
        Assert.Null((await _h.Orders.GetMineAsync(order, _h.Buyer, None)).Value!.Delivery);
    }

    [Fact]
    public async Task Cancelling_the_order_takes_the_delivery_from_the_driver_and_erases_their_position()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);
        await _delivery.RecordLocationAsync(driver, job, new LocationInput(11.93, 79.83), None);

        await _h.Orders.CancelForBusinessAsync(_h.BusinessId, order, 0, _h.Owner, "Out of gas", None);

        var cancelled = await JobOfAsync(order);
        Assert.Equal(DeliveryJobStatus.Cancelled, cancelled.Status);
        Assert.Equal(driver, cancelled.DriverUserId);
        Assert.Null(cancelled.LastLatitude);
        var view = await _delivery.GetMineAsync(driver, None);
        Assert.Null(view.Current);
        Assert.Equal("job_invalid_transition", (await _delivery.PickUpAsync(driver, job, None)).Error!.Code);
    }

    [Fact]
    public async Task A_driver_cannot_accept_a_delivery_that_was_cancelled()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await JobOfAsync(order);
        await _h.Orders.CancelForBusinessAsync(_h.BusinessId, order, 0, _h.Owner, "Out of gas", None);

        Assert.Equal("job_taken", (await _delivery.AcceptAsync(driver, job.Id, None)).Error!.Code);
    }

    // --- The driver's own account ---

    [Fact]
    public async Task A_driver_holding_a_delivery_cannot_go_off_duty_or_change_their_details()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);
        var profile = await _drivers.GetMineAsync(driver, None);

        var off = await _drivers.SetDutyAsync(driver, false, profile.Version, None);
        var edit = await _drivers.SaveMineAsync(driver, new DriverInput("New Name", VehicleType.Bicycle, null, null, profile.Version), None);

        Assert.Equal("driver_has_active_job", off.Error!.Code);
        Assert.Equal("driver_has_active_job", edit.Error!.Code);
        Assert.True((await _drivers.GetMineAsync(driver, None)).IsOnDuty);

        await _delivery.ReleaseAsync(driver, job, None);

        Assert.True((await _drivers.SetDutyAsync(driver, false, profile.Version, None)).IsSuccess);
    }

    [Fact]
    public async Task Suspending_a_driver_returns_a_delivery_they_have_not_collected_to_the_other_drivers()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        await TakenAsync(driver, order);
        var profile = await _drivers.GetMineAsync(driver, None);

        var suspended = await _drivers.SuspendAsync(driver, profile.Version, "Complaint", _h.B.Administrator, None);

        Assert.True(suspended.IsSuccess);
        var job = await JobOfAsync(order);
        Assert.Equal(DeliveryJobStatus.Open, job.Status);
        Assert.Null(job.DriverUserId);
    }

    [Fact]
    public async Task Suspending_a_driver_leaves_a_delivery_they_have_already_collected_with_them()
    {
        var order = await ReadyOrderAsync();
        var driver = await DriverOnDutyAsync();
        var job = await TakenAsync(driver, order);
        await _delivery.PickUpAsync(driver, job, None);
        var profile = await _drivers.GetMineAsync(driver, None);

        await _drivers.SuspendAsync(driver, profile.Version, "Complaint", _h.B.Administrator, None);

        var held = await JobOfAsync(order);
        Assert.Equal(DeliveryJobStatus.PickedUp, held.Status);
        Assert.Equal(driver, held.DriverUserId);
    }

    private static string ErrorKindOf<T>(OnePondy.Application.Common.Result<T> result) =>
        result.Error!.Kind.ToString().ToLowerInvariant();
}
