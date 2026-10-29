using OnePondy.Application.Payments;

namespace OnePondy.Worker;

/// <summary>
/// Sends refunds that were recorded but never reached the payment gateway (it was unreachable when the booking was
/// cancelled). Without this a customer's refund could wait forever for a webhook that is never going to come.
/// </summary>
public sealed class PendingRefundJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<PendingRefundJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 50;

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
            var sent = await scope.ServiceProvider.GetRequiredService<RefundService>().SendUnsentAsync(BatchSize, cancellationToken);
            if (sent > 0)
            {
                logger.LogInformation("Sent {Count} refunds that had not reached the payment gateway", sent);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Sending pending refunds failed");
        }
    }
}
