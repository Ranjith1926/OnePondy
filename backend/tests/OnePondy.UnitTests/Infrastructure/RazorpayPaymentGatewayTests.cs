using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Payments;
using OnePondy.Infrastructure;
using OnePondy.Infrastructure.Payments;

namespace OnePondy.UnitTests.Infrastructure;

public class RazorpayPaymentGatewayTests
{
    private sealed class StubHandler(HttpStatusCode status, string json = "{}", bool throws = false) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (throws)
            {
                throw new HttpRequestException("network down");
            }

            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static RazorpayPaymentGateway Gateway(StubHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new PaymentsOptions { Razorpay = new RazorpayOptions { KeyId = "rzp_test_key", KeySecret = "secret" } }),
            NullLogger<RazorpayPaymentGateway>.Instance);

    private static GatewayOrderRequest Order() => new(100_000, "INR", "receipt1", Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public async Task Creates_an_order_with_the_amount_in_paise_signed_with_the_key_pair()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":"order_abc","amount":100000,"currency":"INR","status":"created"}""");

        var result = await Gateway(handler).CreateOrderAsync(Order(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new GatewayOrder("order_abc", 100_000, "INR"), result.Value);
        Assert.Equal("https://api.razorpay.com/v1/orders", handler.Request!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.Equal("Basic", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("rzp_test_key:secret", Encoding.ASCII.GetString(Convert.FromBase64String(handler.Request.Headers.Authorization.Parameter!)));

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(100_000, body.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal("INR", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal("receipt1", body.RootElement.GetProperty("receipt").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", body.RootElement.GetProperty("notes").GetProperty("booking_id").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_order_is_reported_as_payment_unavailable(HttpStatusCode status)
    {
        var result = await Gateway(new StubHandler(status, """{"error":{"description":"nope"}}""")).CreateOrderAsync(Order(), default);

        Assert.Equal("payments_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task A_network_failure_is_reported_as_payment_unavailable()
    {
        var result = await Gateway(new StubHandler(HttpStatusCode.OK, throws: true)).CreateOrderAsync(Order(), default);

        Assert.Equal("payments_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task An_order_reply_without_an_id_is_not_trusted()
    {
        var result = await Gateway(new StubHandler(HttpStatusCode.OK, """{"amount":100000}""")).CreateOrderAsync(Order(), default);

        Assert.Equal("payments_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task A_garbled_reply_is_not_trusted()
    {
        var result = await Gateway(new StubHandler(HttpStatusCode.OK, "<html>gateway error</html>")).CreateOrderAsync(Order(), default);

        Assert.Equal("payments_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task Refunds_a_payment_by_its_id_for_an_amount_in_paise()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":"rfnd_1","status":"processed"}""");

        var result = await Gateway(handler).RefundAsync(new GatewayRefundRequest("pay_123", 50_000, "refund1"), default);

        Assert.Equal(new GatewayRefund("rfnd_1", "processed"), result.Value);
        Assert.Equal("https://api.razorpay.com/v1/payments/pay_123/refund", handler.Request!.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(50_000, body.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal("refund1", body.RootElement.GetProperty("receipt").GetString());
    }

    [Fact]
    public async Task A_refused_refund_is_reported_so_it_can_be_retried()
    {
        var result = await Gateway(new StubHandler(HttpStatusCode.BadRequest)).RefundAsync(new GatewayRefundRequest("pay_123", 50_000, "refund1"), default);

        Assert.Equal("payments_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task Without_credentials_every_call_reports_online_payment_as_unavailable()
    {
        var gateway = new UnavailablePaymentGateway();

        Assert.Equal("payments_unavailable", (await gateway.CreateOrderAsync(Order(), default)).Error!.Code);
        Assert.Equal("payments_unavailable", (await gateway.RefundAsync(new GatewayRefundRequest("pay_1", 100, "r"), default)).Error!.Code);
    }
}

public class PaymentGatewayRegistrationTests
{
    private sealed class TestEnvironment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "OnePondy.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var all = new Dictionary<string, string?>
        {
            ["ConnectionStrings:OnePondy"] = "Host=localhost;Database=onepondy_test;Username=test",
            ["Auth:Jwt:SigningKey"] = Convert.ToBase64String(new byte[32]),
            ["Auth:HashingKey"] = Convert.ToBase64String(new byte[32]),
        };
        foreach (var (key, value) in settings)
        {
            all[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(all).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(configuration, new TestEnvironment("Production"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void With_no_keys_online_payment_is_unavailable_rather_than_broken()
    {
        using var provider = Build();

        Assert.IsType<UnavailablePaymentGateway>(provider.GetRequiredService<IPaymentGateway>());
    }

    [Fact]
    public void With_a_test_key_pair_the_razorpay_gateway_is_used()
    {
        using var provider = Build(("Payments:Razorpay:KeyId", "rzp_test_abc"), ("Payments:Razorpay:KeySecret", "secret"));

        Assert.IsType<RazorpayPaymentGateway>(provider.GetRequiredService<IPaymentGateway>());
    }

    [Theory]
    [InlineData("Payments:Razorpay:KeyId", "rzp_test_abc")]
    [InlineData("Payments:Razorpay:KeySecret", "secret")]
    public void Half_a_key_pair_stops_the_application_starting(string key, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Build((key, value)));

        Assert.Contains("must be set together", exception.Message);
    }

    [Fact]
    public void A_live_key_pair_is_still_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Build(("Payments:Razorpay:KeyId", "rzp_live_abc"), ("Payments:Razorpay:KeySecret", "secret")));

        Assert.Contains("live payments are not enabled", exception.Message);
    }
}
