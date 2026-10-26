using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OnePondy.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the API. Generating a migration does not
/// connect, so the placeholder connection string is never used.
/// </summary>
public sealed class OnePondyDbContextFactory : IDesignTimeDbContextFactory<OnePondyDbContext>
{
    public OnePondyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OnePondyDbContext>();
        OnePondyDbContext.Configure(options, "Host=localhost;Database=onepondy_design_time");
        return new OnePondyDbContext(options.Options);
    }
}
