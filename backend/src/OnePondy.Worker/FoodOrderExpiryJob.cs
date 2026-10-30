using OnePondy.Application.Food;

namespace OnePondy.Worker;

/// <summary>Closes food orders the restaurant never answered, or the customer never paid for, every minute (asking the gateway first about unpaid ones).</summary>
public sealed class FoodOrderExpiryJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<FoodOrderExpiryJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<FoodOrderExpiry>().ExpireDueAsync(cancellationToken);
            if (result.Expired > 0 || result.Resolved > 0 || result.Deferred > 0)
            {
                logger.LogInformation(
                    "Food orders: {Expired} expired, {Resolved} found paid at the gateway and confirmed, {Deferred} waiting for the gateway to answer",
                    result.Expired,
                    result.Resolved,
                    result.Deferred);
            }

            if (result.Deferred > 0)
            {
                // A restaurant is waiting on these while Razorpay cannot be asked: worth an operator's attention.
                logger.LogWarning("{Count} food orders are waiting because the payment gateway could not be asked", result.Deferred);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Food order expiry failed");
        }
    }
}
