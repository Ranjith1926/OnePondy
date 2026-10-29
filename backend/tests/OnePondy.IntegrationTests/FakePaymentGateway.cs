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

    public Task<Result<GatewayRefund>> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken)
    {
        lock (RefundRequests)
        {
            RefundRequests.Add(request);
        }

        return Task.FromResult(Result<GatewayRefund>.Success(new GatewayRefund($"rfnd_it_{Interlocked.Increment(ref _refunds)}_{Guid.NewGuid():N}", "pending")));
    }
}
