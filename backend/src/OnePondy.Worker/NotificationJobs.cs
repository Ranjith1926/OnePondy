using OnePondy.Application.Notifications;

namespace OnePondy.Worker;

/// <summary>
/// Sends the push notifications the outbox holds, and later checks they arrived (spec Phase 7). Does nothing until
/// <c>Notifications:Enabled</c> is switched on, so a server without Expo set up is unaffected.
/// </summary>
public sealed class OutboxDispatchJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<OutboxDispatchJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);
    private const int BatchSize = 50;
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(1);

    private DateTimeOffset _lastRetention = DateTimeOffset.MinValue;

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
            var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

            var result = await dispatcher.DispatchAsync(BatchSize, cancellationToken);
            if (result.Sent > 0 || result.Discarded > 0 || result.Retrying > 0)
            {
                logger.LogInformation(
                    "Notifications: {Sent} sent, {Discarded} finished without sending, {Retrying} waiting to be retried",
                    result.Sent,
                    result.Discarded,
                    result.Retrying);
            }

            var receipts = await dispatcher.CheckReceiptsAsync(BatchSize * 4, cancellationToken);
            if (receipts > 0)
            {
                logger.LogInformation("Notifications: {Count} delivery receipts recorded", receipts);
            }

            if (time.GetUtcNow() - _lastRetention >= RetentionInterval)
            {
                var (logs, messages) = await scope.ServiceProvider.GetRequiredService<NotificationRetention>().PurgeAsync(cancellationToken);
                _lastRetention = time.GetUtcNow();
                if (logs > 0 || messages > 0)
                {
                    logger.LogInformation("Notifications: removed {Logs} old log rows and {Messages} old messages", logs, messages);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Sending notifications failed");
        }
    }
}

/// <summary>Queues the reminder the day before each confirmed booking starts. The queue is sent by <see cref="OutboxDispatchJob"/>.</summary>
public sealed class BookingReminderJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<BookingReminderJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

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
            var queued = await scope.ServiceProvider.GetRequiredService<BookingReminders>().EnqueueDueAsync(cancellationToken);
            if (queued > 0)
            {
                logger.LogInformation("Queued {Count} booking reminders", queued);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Queueing booking reminders failed");
        }
    }
}
