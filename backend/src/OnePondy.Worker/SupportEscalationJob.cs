using OnePondy.Application.Support;

namespace OnePondy.Worker;

/// <summary>Escalates support requests that missed a time target, every five minutes, so nothing waits unseen.</summary>
public sealed class SupportEscalationJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<SupportEscalationJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

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
            var escalated = await scope.ServiceProvider.GetRequiredService<SupportEscalation>().EscalateOverdueAsync(cancellationToken);
            if (escalated > 0)
            {
                logger.LogWarning("Support: {Count} requests missed a time target and were escalated", escalated);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Support escalation failed");
        }
    }
}
