using Microsoft.EntityFrameworkCore;

namespace OnePondy.Infrastructure.Persistence;

public sealed class OnePondyDbContext(DbContextOptions<OnePondyDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Entity configurations live in Persistence/Configurations (spec §18.1); none exist until Migration 1.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OnePondyDbContext).Assembly);
    }
}
