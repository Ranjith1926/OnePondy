using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;

namespace OnePondy.IntegrationTests;

/// <summary>Runs the API in-process with test keys and a code sender the tests can read from.</summary>
public sealed class ApiFactory(string connectionString, IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    // Nothing listens on port 1, so connections are refused immediately.
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=2";

    public static readonly string SigningKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('s', 32)));
    public static readonly string HashingKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('h', 32)));

    public CapturedCodes Codes { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:OnePondy", connectionString);
        builder.UseSetting("Auth:Jwt:SigningKey", SigningKey);
        builder.UseSetting("Auth:HashingKey", HashingKey);
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "1000");
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services => services.AddSingleton<IOtpSender>(Codes));
    }

    public static string NewIndianMobile() =>
        "9" + Random.Shared.Next(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);
}

public sealed class CapturedCodes : IOtpSender
{
    private readonly Dictionary<string, string> _codes = [];

    public string For(string destination)
    {
        lock (_codes)
        {
            return _codes[destination];
        }
    }

    public Task SendCodeAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        lock (_codes)
        {
            _codes[destination] = code;
        }

        return Task.CompletedTask;
    }

    public Task SendAccountDeletedAsync(OtpChannel channel, string destination, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
