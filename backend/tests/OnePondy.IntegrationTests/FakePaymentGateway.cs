using OnePondy.Application.Common;
using OnePondy.Application.Payments;

namespace OnePondy.IntegrationTests;

/// <summary>A gateway for HTTP tests: nothing leaves the process. An optional delay makes simultaneous requests overlap.</summary>
public sealed class FakePaymentGateway(TimeSpan? orderDelay = null) : IPaymentGateway
{
    private int _orders;
    private int _refunds;

    public int OrdersCreated => _orders;

    public List<GatewayRefundRequest> RefundRequests { get; } = [];

    public async Task<Result<GatewayOrder>> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken)
    {
        if (orderDelay is { } delay)
        {
            await Task.Delay(delay, cancellationToken);
        }

        return Result<GatewayOrder>.Success(
            new GatewayOrder($"order_it_{Guid.NewGuid():N}_{Interlocked.Increment(ref _orders)}", request.AmountPaise, request.Currency));
    }

    /// <summary>What the gateway says it holds for an order. Add a payment to simulate a customer paying.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, List<GatewayPayment>> OrderPayments { get; } = new();

    public bool LookupsFail { get; set; }

    /// <summary>The payments the gateway lists for the daily comparison.</summary>
    public List<GatewayPaymentRecord> ListedPayments { get; } = [];

    public bool ListFails { get; set; }

    public System.Collections.Concurrent.ConcurrentDictionary<string, GatewayRefundDetail> RefundDetails { get; } = new();

    public Task<Result<GatewayPaymentPage>> ListPaymentsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (ListFails)
        {
            return Task.FromResult(Result<GatewayPaymentPage>.Failure(PaymentErrors.Unavailable));
        }

        lock (ListedPayments)
        {
            return Task.FromResult(Result<GatewayPaymentPage>.Success(new GatewayPaymentPage([.. ListedPayments], true)));
        }
    }

    public Task<Result<GatewayRefundDetail>> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
        Task.FromResult(RefundDetails.TryGetValue(refundId, out var detail)
            ? Result<GatewayRefundDetail>.Success(detail)
            : Result<GatewayRefundDetail>.Failure(PaymentErrors.Unavailable));

    public void CustomerPaid(string orderId, string paymentId, long paise)
    {
        var payments = OrderPayments.GetOrAdd(orderId, _ => []);
        lock (payments)
        {
            payments.Add(new GatewayPayment(paymentId, paise, "INR", "captured"));
        }
    }

    public Task<Result<IReadOnlyList<GatewayPayment>>> GetOrderPaymentsAsync(string orderId, CancellationToken cancellationToken)
    {
        if (LookupsFail)
        {
            return Task.FromResult(Result<IReadOnlyList<GatewayPayment>>.Failure(PaymentErrors.Unavailable));
        }

        IReadOnlyList<GatewayPayment> found = [];
        if (OrderPayments.TryGetValue(orderId, out var payments))
        {
            lock (payments)
            {
                found = [.. payments];
            }
        }

        return Task.FromResult(Result<IReadOnlyList<GatewayPayment>>.Success(found));
    }

    public Task<Result<GatewayRefund>> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken)
    {
        lock (RefundRequests)
        {
            RefundRequests.Add(request);
        }

        return Task.FromResult(Result<GatewayRefund>.Success(new GatewayRefund($"rfnd_it_{Interlocked.Increment(ref _refunds)}_{Guid.NewGuid():N}", "pending")));
    }
}
