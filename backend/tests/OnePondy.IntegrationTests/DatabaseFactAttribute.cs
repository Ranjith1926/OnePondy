namespace OnePondy.IntegrationTests;

/// <summary>
/// A test that needs a real PostgreSQL database. It is skipped unless
/// <c>ONEPONDY_TEST_DATABASE</c> holds a connection string.
/// </summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public const string VariableName = "ONEPONDY_TEST_DATABASE";

    public DatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariableName)))
        {
            Skip = $"Set {VariableName} to a PostgreSQL connection string to run database tests.";
        }
    }
}
