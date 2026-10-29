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
            var result = await scope.ServiceProvider.GetRequiredService<BookingHoldExpiry>().ExpireDueHoldsAsync(cancellationToken);
            if (result.Expired > 0 || result.Resolved > 0 || result.Deferred > 0)
            {
                logger.LogInformation(
                    "Booking holds: {Expired} expired, {Resolved} found paid at the gateway and confirmed, {Deferred} waiting for the gateway to answer",
                    result.Expired,
                    result.Resolved,
                    result.Deferred);
            }

            if (result.Deferred > 0)
            {
                // Held seats are not being released because Razorpay cannot be asked: worth an operator's attention.
                logger.LogWarning("{Count} booking holds are waiting because the payment gateway could not be asked", result.Deferred);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Booking hold expiry failed");
        }
    }
}
