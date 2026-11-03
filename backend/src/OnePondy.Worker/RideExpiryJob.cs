using OnePondy.Application.Rides;

namespace OnePondy.Worker;

/// <summary>Gives up on ride requests no driver took in time every 30 seconds, and clears old unused fare quotes.</summary>
public sealed class RideExpiryJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<RideExpiryJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

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
            var expiry = scope.ServiceProvider.GetRequiredService<RideExpiry>();
            var expired = await expiry.ExpireDueAsync(cancellationToken);
            if (expired > 0)
            {
                logger.LogInformation("Rides: {Expired} requests expired with no driver", expired);
            }

            await expiry.DeleteOldQuotesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Ride expiry failed");
        }
    }
}
