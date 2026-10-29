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
        Payments = new PaymentService(
            Bookings.Db,
            Gateway,
            Bookings.Time,
            Options.Create(Bookings.Payments),
            NullLogger<PaymentService>.Instance);
    }

    public BookingHarness Bookings { get; }

    public FakePaymentGateway Gateway { get; } = new();

    public PaymentService Payments { get; }

    /// <summary>A priced booking that the partner has accepted, so it now waits for payment.</summary>
    public async Task<BookingDetail> AwaitingPaymentAsync(string idempotencyKey = "idem-pay")
    {
        await Bookings.EnablePaymentsAsync();
        var created = (await Bookings.Bookings.CreateAsync(Bookings.Customer, Bookings.Input(idempotencyKey: idempotencyKey), default)).Value!;
        return (await Bookings.Bookings.AcceptAsync(Bookings.BusinessId, created.Id, created.Version, Bookings.Owner, default)).Value!;
    }

    public async Task<int> ReservedAsync()
    {
        var slots = await Bookings.Listings.ListPublicSlotsAsync(Bookings.ListingId, null, default);
        return slots.Value!.Single().ReservedCount;
    }

    public async Task<BookingDetail> BookingAsync(Guid id) =>
        (await Bookings.Bookings.GetMineAsync(id, Bookings.Customer, default)).Value!;
}
