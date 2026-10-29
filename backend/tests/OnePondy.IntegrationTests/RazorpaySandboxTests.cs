using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Payments;
using OnePondy.Infrastructure.Payments;

namespace OnePondy.IntegrationTests;

/// <summary>
/// Runs only when <c>ONEPONDY_RAZORPAY_TEST_KEY_ID</c> and <c>ONEPONDY_RAZORPAY_TEST_KEY_SECRET</c> hold a Razorpay
/// <b>test-mode</b> key pair. It talks to Razorpay's real test API, which creates an order and charges nothing.
/// </summary>
public sealed class RazorpaySandboxFactAttribute : FactAttribute
{
    public const string KeyIdVariable = "ONEPONDY_RAZORPAY_TEST_KEY_ID";
    public const string KeySecretVariable = "ONEPONDY_RAZORPAY_TEST_KEY_SECRET";

    public RazorpaySandboxFactAttribute()
    {
        var keyId = Environment.GetEnvironmentVariable(KeyIdVariable);
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeySecretVariable)))
        {
            Skip = $"Set {KeyIdVariable} and {KeySecretVariable} to a Razorpay test key pair to call the sandbox.";
        }
        else if (!keyId.StartsWith("rzp_test_", StringComparison.Ordinal))
        {
            Skip = "Only a Razorpay test key (rzp_test_...) is ever used by tests.";
        }
    }
}

public class RazorpaySandboxTests
{
    private static RazorpayPaymentGateway Gateway(string keyId, string keySecret) =>
        new(
            new HttpClient(),
            Options.Create(new PaymentsOptions { Razorpay = new RazorpayOptions { KeyId = keyId, KeySecret = keySecret } }),
            NullLogger<RazorpayPaymentGateway>.Instance);

    [RazorpaySandboxFact]
    public async Task The_real_client_creates_a_test_order_in_razorpays_sandbox()
    {
        var gateway = Gateway(
            Environment.GetEnvironmentVariable(RazorpaySandboxFactAttribute.KeyIdVariable)!,
            Environment.GetEnvironmentVariable(RazorpaySandboxFactAttribute.KeySecretVariable)!);

        var result = await gateway.CreateOrderAsync(
            new GatewayOrderRequest(Money.ToPaise(1m), "INR", "it" + Guid.NewGuid().ToString("N")[..20], Guid.NewGuid()),
            default);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.StartsWith("order_", result.Value.OrderId);
        Assert.Equal(100, result.Value.AmountPaise);
        Assert.Equal("INR", result.Value.Currency);
    }

    [RazorpaySandboxFact]
    public async Task The_real_client_sees_no_payments_on_a_fresh_order_and_cannot_find_an_unknown_one()
    {
        var gateway = Gateway(
            Environment.GetEnvironmentVariable(RazorpaySandboxFactAttribute.KeyIdVariable)!,
            Environment.GetEnvironmentVariable(RazorpaySandboxFactAttribute.KeySecretVariable)!);
        var order = await gateway.CreateOrderAsync(
            new GatewayOrderRequest(Money.ToPaise(1m), "INR", "it" + Guid.NewGuid().ToString("N")[..20], Guid.NewGuid()),
            default);

        var none = await gateway.GetOrderPaymentsAsync(order.Value!.OrderId, default);
        var unknown = await gateway.GetOrderPaymentsAsync("order_doesnotexist000", default);

        Assert.True(none.IsSuccess, none.Error?.Code);
        Assert.Empty(none.Value);
        // "Could not find out", never "nothing was paid": the Worker keeps the hold when it cannot tell.
        Assert.Equal("payments_unavailable", unknown.Error!.Code);
    }

    [RazorpaySandboxFact]
    public async Task The_real_client_reports_a_wrong_secret_as_payment_unavailable()
    {
        var gateway = Gateway(Environment.GetEnvironmentVariable(RazorpaySandboxFactAttribute.KeyIdVariable)!, "not-the-secret");

        var result = await gateway.CreateOrderAsync(new GatewayOrderRequest(100, "INR", "it-bad", Guid.NewGuid()), default);

        Assert.Equal("payments_unavailable", result.Error!.Code);
    }
}
