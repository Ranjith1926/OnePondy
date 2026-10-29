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
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
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
    public async Task A_refund_carries_its_own_id_as_the_idempotency_key_so_a_repeat_cannot_pay_twice()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":"rfnd_1","status":"pending"}""");

        await Gateway(handler).RefundAsync(new GatewayRefundRequest("pay_123", 50_000, "0123456789abcdef0123456789abcdef"), default);

        Assert.Equal("0123456789abcdef0123456789abcdef", Assert.Single(handler.Request!.Headers.GetValues("X-Refund-Idempotency")));
    }

    [Fact]
    public async Task Creating_an_order_sends_no_refund_idempotency_header()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":"order_abc","amount":100000,"currency":"INR"}""");

        await Gateway(handler).CreateOrderAsync(Order(), default);

        Assert.False(handler.Request!.Headers.Contains("X-Refund-Idempotency"));
    }

    [Fact]
    public async Task A_refused_refund_is_reported_so_it_can_be_retried()
    {
        var result = await Gateway(new StubHandler(HttpStatusCode.BadRequest)).RefundAsync(new GatewayRefundRequest("pay_123", 50_000, "refund1"), default);

        Assert.Equal("payments_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task Lists_the_payments_made_against_an_order_with_a_signed_get()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"entity":"collection","count":2,"items":[{"id":"pay_1","amount":100000,"currency":"INR","status":"failed","email":"x@example.com"},{"id":"pay_2","amount":100000,"currency":"INR","status":"captured"}]}""");

        var result = await Gateway(handler).GetOrderPaymentsAsync("order_abc", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [new GatewayPayment("pay_1", 100_000, "INR", "failed"), new GatewayPayment("pay_2", 100_000, "INR", "captured")],
            result.Value);
        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://api.razorpay.com/v1/orders/order_abc/payments", handler.Request.RequestUri!.ToString());
        Assert.Equal("Basic", handler.Request.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task An_order_with_no_payments_gives_an_empty_list_not_an_error()
    {
        var result = await Gateway(new StubHandler(HttpStatusCode.OK, """{"entity":"collection","count":0,"items":[]}""")).GetOrderPaymentsAsync("order_abc", default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_lookup_is_could_not_find_out_never_nothing_was_paid(HttpStatusCode status)
    {
        var result = await Gateway(new StubHandler(status)).GetOrderPaymentsAsync("order_abc", default);

        Assert.Equal("payments_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task A_network_failure_or_garbled_reply_on_a_lookup_is_could_not_find_out()
    {
        Assert.Equal("payments_unavailable", (await Gateway(new StubHandler(HttpStatusCode.OK, throws: true)).GetOrderPaymentsAsync("order_abc", default)).Error!.Code);
        Assert.Equal("payments_unavailable", (await Gateway(new StubHandler(HttpStatusCode.OK, "<html>")).GetOrderPaymentsAsync("order_abc", default)).Error!.Code);
        Assert.Equal("payments_unavailable", (await Gateway(new StubHandler(HttpStatusCode.OK, "{}")).GetOrderPaymentsAsync("order_abc", default)).Error!.Code);
    }

    /// <summary>Answers a payments list one page at a time, the way Razorpay does, and remembers what it was asked.</summary>
    private sealed class PagedHandler(int totalPayments) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var skip = int.Parse(query["skip"]!, System.Globalization.CultureInfo.InvariantCulture);
            var count = int.Parse(query["count"]!, System.Globalization.CultureInfo.InvariantCulture);
            var items = Enumerable.Range(skip, Math.Max(0, Math.Min(count, totalPayments - skip)))
                .Select(i => new { id = "pay_" + i, order_id = "order_" + i, amount = 100_000, amount_refunded = i % 2 == 0 ? 0 : 25_000, currency = "INR", status = "captured" })
                .ToList();
            var json = System.Text.Json.JsonSerializer.Serialize(new { entity = "collection", count = items.Count, items });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private static RazorpayPaymentGateway Gateway(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new PaymentsOptions { Razorpay = new RazorpayOptions { KeyId = "rzp_test_key", KeySecret = "secret" } }),
            NullLogger<RazorpayPaymentGateway>.Instance);

    [Fact]
    public async Task Lists_a_periods_payments_asking_razorpay_by_unix_time_and_reading_each_field()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"entity":"collection","count":1,"items":[{"id":"pay_1","order_id":"order_1","amount":100000,"amount_refunded":25000,"currency":"INR","status":"captured","email":"x@example.com"}]}""");
        var from = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

        var result = await Gateway(handler).ListPaymentsAsync(from, from.AddDays(1), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Complete);
        Assert.Equal([new GatewayPaymentRecord("pay_1", "order_1", 100_000, 25_000, "INR", "captured")], result.Value.Payments);
        var query = System.Web.HttpUtility.ParseQueryString(handler.Request!.RequestUri!.Query);
        Assert.Equal(from.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), query["from"]);
        Assert.Equal(from.AddDays(1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), query["to"]);
        Assert.Equal(HttpMethod.Get, handler.Request.Method);
        Assert.Equal("Basic", handler.Request.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task A_busy_day_is_fetched_page_after_page_until_the_last_short_page()
    {
        var handler = new PagedHandler(totalPayments: 230);

        var result = await Gateway(handler).ListPaymentsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, default);

        Assert.True(result.Value!.Complete);
        Assert.Equal(230, result.Value.Payments.Count);
        Assert.Equal(230, result.Value.Payments.Select(p => p.PaymentId).Distinct().Count());
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task A_day_with_exactly_a_full_page_still_asks_for_the_next_one()
    {
        var handler = new PagedHandler(totalPayments: 100);

        var result = await Gateway(handler).ListPaymentsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, default);

        Assert.True(result.Value!.Complete);
        Assert.Equal(100, result.Value.Payments.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_list_longer_than_the_page_limit_says_it_is_incomplete_rather_than_pretending_to_be_whole()
    {
        var handler = new PagedHandler(totalPayments: 1_000_000);

        var result = await Gateway(handler).ListPaymentsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, default);

        Assert.False(result.Value!.Complete);
        Assert.Equal(5_000, result.Value.Payments.Count);
        Assert.Equal(50, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_list_is_reported_as_could_not_find_out(HttpStatusCode status)
    {
        var result = await Gateway(new StubHandler(status)).ListPaymentsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, default);

        Assert.Equal("payments_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task Fetches_one_refund_with_its_status()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":"rfnd_1","payment_id":"pay_1","amount":25000,"status":"processed"}""");

        var result = await Gateway(handler).GetRefundAsync("rfnd_1", default);

        Assert.Equal(new GatewayRefundDetail("rfnd_1", "pay_1", 25_000, "processed"), result.Value);
        Assert.Equal("https://api.razorpay.com/v1/refunds/rfnd_1", handler.Request!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Request.Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_refund_the_gateway_cannot_find_is_could_not_find_out(HttpStatusCode status)
    {
        var result = await Gateway(new StubHandler(status)).GetRefundAsync("rfnd_1", default);

        Assert.Equal("payments_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task Without_credentials_listing_and_refund_lookups_report_online_payment_as_unavailable()
    {
        var gateway = new UnavailablePaymentGateway();

        Assert.Equal("payments_unavailable", (await gateway.ListPaymentsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, default)).Error!.Code);
        Assert.Equal("payments_unavailable", (await gateway.GetRefundAsync("rfnd_1", default)).Error!.Code);
    }

    [Fact]
    public async Task Without_credentials_every_call_reports_online_payment_as_unavailable()
    {
        var gateway = new UnavailablePaymentGateway();

        Assert.Equal("payments_unavailable", (await gateway.CreateOrderAsync(Order(), default)).Error!.Code);
        Assert.Equal("payments_unavailable", (await gateway.RefundAsync(new GatewayRefundRequest("pay_1", 100, "r"), default)).Error!.Code);
        Assert.Equal("payments_unavailable", (await gateway.GetOrderPaymentsAsync("order_1", default)).Error!.Code);
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
