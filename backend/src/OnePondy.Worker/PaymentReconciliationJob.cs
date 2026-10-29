using OnePondy.Application.Payments;

namespace OnePondy.Worker;

/// <summary>
/// The safety net under payment webhooks (spec Phase 7: "reconciliation finds and resolves failed or delayed
/// callbacks"). Every five minutes it checks recent orders, refunds, webhooks and ledger entries; once a day it
/// compares the previous day's payments with the gateway's. Differences are recorded for an administrator, never
/// fixed silently (see <see cref="ReconciliationService"/>).
/// </summary>
public sealed class PaymentReconciliationJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<PaymentReconciliationJob> logger) : BackgroundService
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
            var reconciliation = scope.ServiceProvider.GetRequiredService<ReconciliationService>();

            var frequent = await reconciliation.RunFrequentChecksAsync(cancellationToken);
            if (frequent.Raised > 0 || frequent.AutoResolved > 0)
            {
                logger.LogWarning(
                    "Payment reconciliation: {Raised} new problems for review, {AutoResolved} fixed from the gateway",
                    frequent.Raised,
                    frequent.AutoResolved);
            }

            if (!frequent.GatewayReachable)
            {
                logger.LogWarning("Payment reconciliation could not reach the payment gateway; it will try again");
            }

            var days = await reconciliation.RunDueDailyChecksAsync(cancellationToken);
            if (days > 0)
            {
                logger.LogInformation("Payment reconciliation completed for {Days} day(s)", days);
            }

            var open = await scope.ServiceProvider.GetRequiredService<ReconciliationExceptionService>().OpenCountAsync(cancellationToken);
            if (open > 0)
            {
                // Repeated every run on purpose: an alert on this line keeps firing until someone resolves them.
                logger.LogWarning("{Count} payment reconciliation problems are waiting for an administrator", open);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Payment reconciliation failed");
        }
    }
}
