using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Food;
using OnePondy.Application.Notifications;
using OnePondy.Application.Payments;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Food;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Payments;
using OnePondy.UnitTests.Bookings;
using OnePondy.UnitTests.Businesses;
using OnePondy.UnitTests.Identity;

namespace OnePondy.UnitTests.Food;

public sealed record FoodPaymentServices(LedgerService Ledger, RefundService Refunds, PaymentService Payments);

/// <summary>
/// Restaurants, food orders and the Phase 7 payment services on one in-memory database, with a fake gateway and a controllable clock.
/// One open restaurant with a dosa (₹120) and a coffee (₹45) at 5% GST; the default order is two dosas and a coffee.
/// </summary>
public sealed class FoodOrderHarness
{
    public const string KeyId = "rzp_test_food";

    /// <summary>Two dosas and a coffee: 285 of food, 14.25 GST, 30 delivery and 10 packaging.</summary>
    public const decimal Total = 339.25m;

    public const decimal Subtotal = 285m;

    public FoodOrderHarness()
    {
        Payments.Razorpay.KeyId = KeyId;
        var audit = new AuditTrail(B.Db, B.Time);
        Restaurants = new RestaurantService(B.Db, audit, B.Time);
        Menu = new MenuService(B.Db, Restaurants, audit, B.Time);
        var services = PaymentServices(B.Db, B.Time, Payments, Gateway);
        Ledger = services.Ledger;
        Refunds = services.Refunds;
        PaymentService = services.Payments;
        Orders = new OrderService(B.Db, Restaurants, Refunds, Options.Create(Food), B.Time);
        Expiry = new FoodOrderExpiry(B.Db, Orders, PaymentService, B.Time, Options.Create(Food));
        Settlements = new SettlementService(B.Db, Ledger, audit, B.Time);
        Reconciliation = new ReconciliationService(B.Db, Gateway, PaymentService, B.Time, NullLogger<ReconciliationService>.Instance);
        PaymentAdmin = new PaymentAdminService(B.Db);
        Owner = B.Customer;
        Buyer = B.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000044444", default).Result.Id;
        Stranger = B.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000055555", default).Result.Id;
        Driver = B.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000066666", default).Result.Id;
    }

    public static FoodPaymentServices PaymentServices(TestDbContext db, TimeProvider time, PaymentsOptions options, FakePaymentGateway gateway)
    {
        var wrapped = Options.Create(options);
        var ledger = new LedgerService(db, time, wrapped);
        var refunds = new RefundService(db, gateway, time, wrapped, NullLogger<RefundService>.Instance);
        var payments = new PaymentService(
            db, gateway, time, wrapped, ledger, refunds, new NotificationOutbox(db, time), NullLogger<PaymentService>.Instance);
        return new FoodPaymentServices(ledger, refunds, payments);
    }

    public BusinessHarness B { get; } = new();

    public TestDbContext Db => B.Db;

    public PaymentsOptions Payments { get; } = new();

    public FoodOptions Food { get; } = new();

    public FakePaymentGateway Gateway { get; } = new();

    public RestaurantService Restaurants { get; }

    public MenuService Menu { get; }

    public LedgerService Ledger { get; }

    public RefundService Refunds { get; }

    public PaymentService PaymentService { get; }

    public OrderService Orders { get; }

    public FoodOrderExpiry Expiry { get; }

    public SettlementService Settlements { get; }

    public ReconciliationService Reconciliation { get; }

    public PaymentAdminService PaymentAdmin { get; }

    public Guid Owner { get; }

    public Guid Buyer { get; }

    public Guid Stranger { get; }

    public Guid Driver { get; }

    public Guid BusinessId { get; private set; }

    private Guid _dosa;
    private Guid _coffee;

    /// <summary>Opens the restaurant (food module on, online payment on unless told otherwise).</summary>
    public async Task OpenAsync(bool paymentsOn = true)
    {
        B.Db.ModuleFlags.Add(new ModuleFlag(ModuleKeys.Food, ModuleStatus.Available));
        if (paymentsOn)
        {
            B.Db.ModuleFlags.Add(new ModuleFlag(ModuleKeys.Payments, ModuleStatus.Available));
        }

        await B.Db.SaveChangesAsync();
        var applied = await B.Businesses.ApplyAsync(Owner, BusinessHarness.Input("Spice Route", OnePondy.Domain.Businesses.BusinessType.Restaurant, "12345678901234"), default);
        BusinessId = (await B.Businesses.ApproveAsync(applied.Value!.Id, applied.Value.Version, B.Administrator, default)).Value!.Id;
        var category = (await Menu.CreateCategoryAsync(BusinessId, Owner, new MenuCategoryInput("Breakfast", null, null), default)).Value!.Id;
        _dosa = (await Menu.CreateItemAsync(BusinessId, Owner, new MenuItemInput(category, "Masala dosa", null, 120m, true, null, null), default)).Value!.Id;
        _coffee = (await Menu.CreateItemAsync(BusinessId, Owner, new MenuItemInput(category, "Filter coffee", null, 45m, true, null, null), default)).Value!.Id;
        var saved = await Restaurants.SaveMineAsync(BusinessId, Owner, new RestaurantInput(25, 100m, true, ["whiteTown"], null), default);
        Assert.True(saved.IsSuccess, saved.Error?.Detail);
    }

    public async Task<OrderDetail> PlacedAsync(string key = "order-1", Guid? customer = null)
    {
        var input = new PlaceOrderInput(
            key, BusinessId, "whiteTown", "12 Rue Suffren, White Town", null, "+919876500001", [new(_dosa, 2), new(_coffee, 1)], Total);
        var placed = await Orders.PlaceAsync(customer ?? Buyer, input, default);
        Assert.True(placed.IsSuccess, placed.Error?.Detail);
        return placed.Value!;
    }

    /// <summary>An order the restaurant accepted, now waiting for the customer to pay.</summary>
    public async Task<OrderDetail> AwaitingPaymentAsync(string key = "order-1")
    {
        var placed = await PlacedAsync(key);
        var accepted = await Orders.AcceptAsync(BusinessId, placed.Id, placed.Version, Owner, default);
        Assert.Equal(FoodOrderStatus.AwaitingPayment, accepted.Value!.Status);
        return (await MineAsync(placed.Id))!;
    }

    /// <summary>An order that was accepted and paid: confirmed, with the payment booked to the ledger.</summary>
    public async Task<(OrderDetail Order, string GatewayOrderId, string PaymentId)> PaidAsync(string key = "order-1")
    {
        var awaiting = await AwaitingPaymentAsync(key);
        var started = (await PaymentService.StartFoodOrderPaymentAsync(awaiting.Id, Buyer, default)).Value!;
        var paymentId = "pay_" + key;
        var captured = await PaymentService.RecordCaptureAsync(started.OrderId, paymentId, Total, default);
        Assert.Equal(CaptureOutcome.Confirmed, captured.Value!.Outcome);
        return ((await MineAsync(awaiting.Id))!, started.OrderId, paymentId);
    }

    /// <summary>Drives a confirmed order through the kitchen and (through the domain, until the driver app exists) to delivered.</summary>
    public async Task DeliverAsync(Guid orderId)
    {
        Assert.True((await Orders.StartPreparingAsync(BusinessId, orderId, 0, Owner, default)).IsSuccess);
        Assert.True((await Orders.MarkReadyAsync(BusinessId, orderId, 0, Owner, default)).IsSuccess);
        var order = await Db.FoodOrders.Include(o => o.History).SingleAsync(o => o.Id == orderId);
        var before = order.History.Count;
        order.PickUp(Driver, B.Time.GetUtcNow());
        order.Deliver(Driver, B.Time.GetUtcNow());
        for (var i = before; i < order.History.Count; i++)
        {
            Db.MarkAdded(order.History[i]);
        }

        await Db.SaveChangesAsync();
    }

    public async Task<OrderDetail?> MineAsync(Guid id) => (await Orders.GetMineAsync(id, Buyer, default)).Value;

    public async Task<IReadOnlyList<LedgerEntry>> LedgerAsync() => await Db.LedgerEntries.ToListAsync();

    public async Task<decimal> BalanceAsync(string account) => (await LedgerAsync()).Where(e => e.Account == account).Sum(e => e.Amount);

    public async Task<IReadOnlyList<PaymentAttempt>> AttemptsAsync(Guid orderId) => await Db.PaymentAttempts.Where(a => a.FoodOrderId == orderId).ToListAsync();
}
