using OnePondy.Application.Bookings;

namespace OnePondy.Worker;

/// <summary>Releases expired booking holds every minute, so inventory a customer never paid for frees up quickly.</summary>
public sealed class BookingHoldExpiryJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<BookingHoldExpiryJob> logger) : BackgroundService
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
            var expired = await scope.ServiceProvider.GetRequiredService<BookingHoldExpiry>().ExpireDueHoldsAsync(cancellationToken);
            if (expired > 0)
            {
                logger.LogInformation("Expired {Count} booking holds", expired);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Booking hold expiry failed");
        }
    }
}
