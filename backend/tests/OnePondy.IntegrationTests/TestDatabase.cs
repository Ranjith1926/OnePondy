using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.IntegrationTests;

/// <summary>
/// Migrates the test database once per test run. Test classes run in parallel, and two concurrent
/// migrations of an empty database would race to create the same tables.
/// </summary>
internal static class TestDatabase
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _migrated;

    public static async Task MigrateOnceAsync(IServiceProvider services)
    {
        await Gate.WaitAsync();
        try
        {
            if (_migrated)
            {
                return;
            }

            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OnePondyDbContext>().Database.MigrateAsync();
            _migrated = true;
        }
        finally
        {
            Gate.Release();
        }
    }
}
