using OnePondy.Application.Food;

namespace OnePondy.Worker;

/// <summary>Closes food orders the restaurant never answered, or the customer never paid for, every minute.</summary>
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
            var expired = await scope.ServiceProvider.GetRequiredService<OrderService>().ExpireDueAsync(cancellationToken);
            if (expired > 0)
            {
                logger.LogInformation("Food orders: {Expired} expired", expired);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Food order expiry failed");
        }
    }
}
