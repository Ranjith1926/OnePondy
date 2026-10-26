using OnePondy.Application.Identity;

namespace OnePondy.Worker;

/// <summary>Hourly purge of expired one-time codes, refresh tokens and sessions (docs/data-retention.md).</summary>
public sealed class AuthDataRetentionJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<AuthDataRetentionJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

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
            var purged = await scope.ServiceProvider.GetRequiredService<AuthDataRetention>().PurgeAsync(cancellationToken);
            logger.LogInformation(
                "Purged {Challenges} codes, {RefreshTokens} refresh tokens and {Sessions} sessions",
                purged.Challenges,
                purged.RefreshTokens,
                purged.Sessions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the service running; the next interval retries.
            logger.LogError(exception, "Authentication data purge failed");
        }
    }
}
