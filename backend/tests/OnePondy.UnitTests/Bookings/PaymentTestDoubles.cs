using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Bookings;
using OnePondy.Application.Common;
using OnePondy.Application.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>A gateway that records what it was asked and can be told to fail. Nothing leaves the process.</summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private int _orders;
    private int _refunds;

    public List<GatewayOrderRequest> OrderRequests { get; } = [];

    public List<GatewayRefundRequest> RefundRequests { get; } = [];

    public bool Unavailable { get; set; }

    public bool RefundsFail { get; set; }

    /// <summary>What the gateway says it holds for each order: tests add a payment to simulate a customer paying.</summary>
    public Dictionary<string, List<GatewayPayment>> OrderPayments { get; } = [];

    /// <summary>Makes every lookup of the payments for an order fail, as if the gateway were unreachable.</summary>
    public bool LookupsFail { get; set; }

    public int Lookups { get; private set; }

    // --- What the gateway would list and report, for reconciliation ---

    public List<GatewayPaymentRecord> ListedPayments { get; } = [];

    /// <summary>The gateway had more payments than one fetch returns.</summary>
    public bool ListIncomplete { get; set; }

    public bool ListFails { get; set; }

    public int Listings { get; private set; }

    public Dictionary<string, GatewayRefundDetail> RefundDetails { get; } = [];

    public bool RefundLookupsFail { get; set; }

    public int RefundLookups { get; private set; }

    public void ListPayment(string orderId, string paymentId, long paise, string status = "captured", long refundedPaise = 0) =>
        ListedPayments.Add(new GatewayPaymentRecord(paymentId, orderId, paise, refundedPaise, "INR", status));

    public void SetRefundStatus(string refundId, string status, long paise = 0) =>
        RefundDetails[refundId] = new GatewayRefundDetail(refundId, null, paise, status);

    public Task<Result<GatewayPaymentPage>> ListPaymentsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        Listings++;
        return Task.FromResult(ListFails
            ? Result<GatewayPaymentPage>.Failure(PaymentErrors.Unavailable)
            : Result<GatewayPaymentPage>.Success(new GatewayPaymentPage([.. ListedPayments], !ListIncomplete)));
    }

    public Task<Result<GatewayRefundDetail>> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        RefundLookups++;
        if (RefundLookupsFail || !RefundDetails.TryGetValue(refundId, out var detail))
        {
            return Task.FromResult(Result<GatewayRefundDetail>.Failure(PaymentErrors.Unavailable));
        }

        return Task.FromResult(Result<GatewayRefundDetail>.Success(detail));
    }

    public void CustomerPaid(string orderId, string paymentId, long paise, string status = "captured")
    {
        if (!OrderPayments.TryGetValue(orderId, out var payments))
        {
            OrderPayments[orderId] = payments = [];
        }

        payments.Add(new GatewayPayment(paymentId, paise, "INR", status));
    }

    public Task<Result<IReadOnlyList<GatewayPayment>>> GetOrderPaymentsAsync(string orderId, CancellationToken cancellationToken)
    {
        Lookups++;
        if (LookupsFail)
        {
            return Task.FromResult(Result<IReadOnlyList<GatewayPayment>>.Failure(PaymentErrors.Unavailable));
        }

        IReadOnlyList<GatewayPayment> found = OrderPayments.TryGetValue(orderId, out var payments) ? [.. payments] : [];
        return Task.FromResult(Result<IReadOnlyList<GatewayPayment>>.Success(found));
    }

    public Task<Result<GatewayOrder>> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            return Task.FromResult(Result<GatewayOrder>.Failure(PaymentErrors.Unavailable));
        }

        OrderRequests.Add(request);
        return Task.FromResult(Result<GatewayOrder>.Success(
            new GatewayOrder($"order_test_{Interlocked.Increment(ref _orders)}", request.AmountPaise, request.Currency)));
    }

    public Task<Result<GatewayRefund>> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken)
    {
        if (RefundsFail)
        {
            return Task.FromResult(Result<GatewayRefund>.Failure(PaymentErrors.Unavailable));
        }

        RefundRequests.Add(request);
        return Task.FromResult(Result<GatewayRefund>.Success(
            new GatewayRefund($"rfnd_test_{Interlocked.Increment(ref _refunds)}", "pending")));
    }
}

/// <summary>The booking harness plus a payment service on a fake gateway, with payments switched on.</summary>
public sealed class PaymentHarness
{
    public const string KeyId = "rzp_test_unit";

    public PaymentHarness()
    {
        Bookings = new BookingHarness();
        Bookings.Payments.Razorpay.KeyId = KeyId;
    }

    public BookingHarness Bookings { get; }

    public FakePaymentGateway Gateway => Bookings.Gateway;

    public PaymentService Payments => Bookings.PaymentService;

    /// <summary>A priced booking that the partner has accepted, so it now waits for payment.</summary>
    public async Task<BookingDetail> AwaitingPaymentAsync(string idempotencyKey = "idem-pay")
    {
        await Bookings.EnablePaymentsAsync();
        var created = (await Bookings.Bookings.CreateAsync(Bookings.Customer, Bookings.Input(idempotencyKey: idempotencyKey), default)).Value!;
        return (await Bookings.Bookings.AcceptAsync(Bookings.BusinessId, created.Id, created.Version, Bookings.Owner, default)).Value!;
    }

    /// <summary>A booking that was accepted and paid: confirmed, with the payment applied and booked to the ledger.</summary>
    public async Task<(BookingDetail Booking, string OrderId, string PaymentId)> PaidAsync(string idempotencyKey = "idem-paid")
    {
        var awaiting = await AwaitingPaymentAsync(idempotencyKey);
        var started = (await Payments.StartPaymentAsync(awaiting.Id, Bookings.Customer, default)).Value!;
        var paymentId = "pay_" + idempotencyKey;
        await Payments.RecordCaptureAsync(started.OrderId, paymentId, 1000m, default);
        return (await BookingAsync(awaiting.Id), started.OrderId, paymentId);
    }

    public async Task<IReadOnlyList<OnePondy.Domain.Payments.LedgerEntry>> LedgerAsync() =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(Bookings.Db.LedgerEntries);

    /// <summary>The balance of one ledger account: debits positive, credits negative.</summary>
    public async Task<decimal> BalanceAsync(string account) => (await LedgerAsync()).Where(e => e.Account == account).Sum(e => e.Amount);

    public async Task<int> ReservedAsync()
    {
        var slots = await Bookings.Listings.ListPublicSlotsAsync(Bookings.ListingId, null, default);
        return slots.Value!.Single().ReservedCount;
    }

    public async Task<BookingDetail> BookingAsync(Guid id) =>
        (await Bookings.Bookings.GetMineAsync(id, Bookings.Customer, default)).Value!;
}
