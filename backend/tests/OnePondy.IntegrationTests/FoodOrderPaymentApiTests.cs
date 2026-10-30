using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OnePondy.Application.Audit;
using OnePondy.Application.Food;
using OnePondy.Application.Notifications;
using OnePondy.Application.Payments;
using OnePondy.Domain.Food;
using OnePondy.Domain.Payments;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.IntegrationTests;

/// <summary>
/// Paying for food orders (spec Phase 8, step 8.3) against a real, migrated PostgreSQL database: the HTTP flow from accepted to
/// paid and refunded, simultaneous requests, settling with the restaurant, and the rules only the database can enforce. Part of
/// <see cref="FoodOrderApiTests"/> so these never run while another test has the food module switched off. The payments module flag is
/// never switched on (other test classes rely on it being off): an order is moved to awaiting payment through the domain, as the accept
/// endpoint does when payments are on.
/// </summary>
public sealed partial class FoodOrderApiTests
{
    private const string WebhookSecret = "integration-webhook-secret";

    private WebApplicationFactory<Program> WithFakeGateway(FakePaymentGateway gateway, bool refundsEnabled = false) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Payments:RefundsEnabled", refundsEnabled ? "true" : "false");
            builder.UseSetting("Payments:Razorpay:KeyId", "rzp_test_integration");
            builder.UseSetting("Payments:Razorpay:KeySecret", "integration-secret");
            builder.UseSetting("Payments:Razorpay:WebhookSecret", WebhookSecret);
            builder.ConfigureTestServices(services => services.AddSingleton<IPaymentGateway>(gateway));
        });

    private sealed record Awaiting(Shop Shop, string Customer, Guid OrderId);

    /// <summary>A placed order the restaurant has accepted for online payment.</summary>
    private async Task<Awaiting> AwaitingPaymentOrderAsync(HttpClient client, string? shopName = null, Shop? shop = null, string? customer = null)
    {
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        shop ??= await OpenShopAsync(client, admin, UniqueName(shopName ?? "Paid kitchen"));
        customer ??= await SignInAsync(client);
        var orderId = Guid.Parse(Id(await PlaceAsync(client, customer, shop)));

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var order = await db.FoodOrders.SingleAsync(o => o.Id == orderId);
        var owner = await OwnerOfAsync(db, order.BusinessId);
        order.Accept(owner, DateTimeOffset.UtcNow, requiresPayment: true, TimeSpan.FromMinutes(10));
        db.MarkAdded(order.History[^1]);
        await db.SaveChangesAsync();
        return new Awaiting(shop, customer, orderId);
    }

    private static Task<Guid> OwnerOfAsync(OnePondyDbContext db, Guid businessId) =>
        db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.OwnerUserId).SingleAsync();

    private static string PaymentCapturedJson(string orderId, string paymentId, long paise) =>
        JsonSerializer.Serialize(new
        {
            entity = "event",
            @event = "payment.captured",
            payload = new { payment = new { entity = new { id = paymentId, order_id = orderId, amount = paise, currency = "INR", status = "captured" } } },
        });

    private static string RefundProcessedJson(string refundId, string paymentId, long paise) =>
        JsonSerializer.Serialize(new { entity = "event", @event = "refund.processed", payload = new { refund = new { entity = new { id = refundId, payment_id = paymentId, amount = paise, status = "processed" } } } });

    private static async Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, string json, string eventId)
    {
        var body = System.Text.Encoding.UTF8.GetBytes(json);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Razorpay-Signature", RazorpaySignature.Compute(WebhookSecret, body));
        request.Headers.Add("X-Razorpay-Event-Id", eventId);
        return await client.SendAsync(request);
    }

    private static string NewEventId() => "evt_food_" + Guid.NewGuid().ToString("N");

    private async Task<FoodOrderStatus> OrderStatusAsync(Guid orderId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await DbOf(scope).FoodOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync();
    }

    private async Task<List<LedgerEntry>> LedgerForAsync(Guid orderId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await DbOf(scope).LedgerEntries.AsNoTracking().Where(e => e.FoodOrderId == orderId).ToListAsync();
    }

    // --- Tests ---

    [DatabaseFact]
    public async Task A_customer_pays_an_accepted_order_and_the_ledger_books_the_food_split()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (shop, customer, orderId) = await AwaitingPaymentOrderAsync(client);

        var first = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/payment", customer));
        var again = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/payment", customer));
        var gatewayOrder = first.GetProperty("orderId").GetString()!;
        Assert.Equal(gatewayOrder, again.GetProperty("orderId").GetString());
        Assert.Equal(orderId.ToString(), first.GetProperty("foodOrderId").GetString());
        Assert.Equal(33925, first.GetProperty("amountPaise").GetInt64());
        Assert.Equal("rzp_test_integration", first.GetProperty("keyId").GetString());
        var request = Assert.Single(gateway.OrderRequests);
        Assert.Equal((null, orderId), (request.BookingId, request.FoodOrderId));

        var stranger = await SignInAsync(client);
        var notYours = await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/payment", stranger);
        Assert.Equal(HttpStatusCode.NotFound, notYours.StatusCode);

        var paymentId = "pay_" + Guid.NewGuid().ToString("N");
        var hook = await PostWebhookAsync(client, PaymentCapturedJson(gatewayOrder, paymentId, 33925), NewEventId());
        Assert.Equal(HttpStatusCode.OK, hook.StatusCode);

        var seen = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/payment/refresh", customer));
        Assert.Equal("confirmed", seen.GetProperty("status").GetString());
        Assert.Equal(paymentId, seen.GetProperty("payment").GetProperty("reference").GetString());
        Assert.Equal(339.25m, seen.GetProperty("payment").GetProperty("amount").GetDecimal());

        // Committed through the database's balance trigger and account check, with the two new accounts.
        var ledger = await LedgerForAsync(orderId);
        Assert.Equal(5, ledger.Count);
        Assert.Equal(0m, ledger.Sum(e => e.Amount));
        Assert.All(ledger, e => Assert.Null(e.BookingId));
        Assert.Equal(-28.50m, ledger.Single(e => e.Account == LedgerAccounts.PlatformCommission).Amount);
        Assert.Equal(-266.50m, ledger.Single(e => e.Account == LedgerAccounts.PartnerPayable(Guid.Parse(shop.BusinessId))).Amount);
        Assert.Equal(-30m, ledger.Single(e => e.Account == LedgerAccounts.DeliveryFees).Amount);
        Assert.Equal(-14.25m, ledger.Single(e => e.Account == LedgerAccounts.TaxPayable).Amount);
    }

    [DatabaseFact]
    public async Task Cancelling_a_paid_order_refunds_it_in_full_and_the_refund_webhook_finishes_it()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway, refundsEnabled: true);
        using var client = factory.CreateClient();
        var (_, customer, orderId) = await AwaitingPaymentOrderAsync(client, "Refunding kitchen");
        var gatewayOrder = (await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/payment", customer))).GetProperty("orderId").GetString()!;
        var paymentId = "pay_" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(client, PaymentCapturedJson(gatewayOrder, paymentId, 33925), NewEventId())).StatusCode);
        var paid = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer));

        var cancelled = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/cancel", customer, new { version = VersionOf(paid), reason = "Ordered twice" }));

        Assert.Equal("refundPending", cancelled.GetProperty("status").GetString());
        var refund = cancelled.GetProperty("refunds")[0];
        Assert.Equal((339.25m, "customerCancelled", "pending"), (refund.GetProperty("amount").GetDecimal(), refund.GetProperty("reason").GetString(), refund.GetProperty("status").GetString()));
        GatewayRefundRequest sent;
        lock (gateway.RefundRequests)
        {
            sent = Assert.Single(gateway.RefundRequests);
        }

        Assert.Equal((paymentId, 33925L), (sent.PaymentId, sent.AmountPaise));

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var providerRefundId = await DbOf(scope).Refunds.AsNoTracking().Where(r => r.FoodOrderId == orderId).Select(r => r.ProviderRefundId).SingleAsync();
            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(client, RefundProcessedJson(providerRefundId!, paymentId, 33925), NewEventId())).StatusCode);
        }

        Assert.Equal(FoodOrderStatus.Refunded, await OrderStatusAsync(orderId));
        var ledger = await LedgerForAsync(orderId);
        Assert.Equal(10, ledger.Count);
        Assert.All(ledger.GroupBy(e => e.Account), account => Assert.Equal(0m, account.Sum(e => e.Amount)));
    }

    [DatabaseFact]
    public async Task With_refunds_off_a_paid_order_cannot_be_cancelled_and_nothing_changes()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, customer, orderId) = await AwaitingPaymentOrderAsync(client, "No refunds kitchen");
        var gatewayOrder = (await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/payment", customer))).GetProperty("orderId").GetString()!;
        await PostWebhookAsync(client, PaymentCapturedJson(gatewayOrder, "pay_" + Guid.NewGuid().ToString("N"), 33925), NewEventId());
        var paid = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer));

        var refused = await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/cancel", customer, new { version = VersionOf(paid) });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("refunds_not_enabled", await ErrorCodeAsync(refused));
        Assert.Equal(FoodOrderStatus.Confirmed, await OrderStatusAsync(orderId));
    }

    [DatabaseFact]
    public async Task Many_simultaneous_requests_to_pay_for_one_order_share_one_gateway_order()
    {
        var gateway = new FakePaymentGateway(TimeSpan.FromMilliseconds(150));
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, customer, orderId) = await AwaitingPaymentOrderAsync(client, "Busy kitchen");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/payment", customer)));

        var orders = new HashSet<string>();
        foreach (var response in responses)
        {
            orders.Add((await ReadAsync(response)).GetProperty("orderId").GetString()!);
        }

        Assert.Single(orders);
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await DbOf(scope).PaymentAttempts.AsNoTracking().CountAsync(a => a.FoodOrderId == orderId && a.Status == PaymentAttemptStatus.Created));
    }

    [DatabaseFact]
    public async Task A_payment_and_a_cancellation_at_the_same_moment_end_one_consistent_way_every_time()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var first = await AwaitingPaymentOrderAsync(client, "Race kitchen");

        for (var round = 0; round < 5; round++)
        {
            var (_, customer, orderId) = round == 0 ? first : await AwaitingPaymentOrderAsync(client, shop: first.Shop, customer: first.Customer);
            var gatewayOrder = (await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/payment", customer))).GetProperty("orderId").GetString()!;
            var awaiting = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer));
            var paymentId = "pay_" + Guid.NewGuid().ToString("N");
            var eventId = NewEventId();
            var json = PaymentCapturedJson(gatewayOrder, paymentId, 33925);

            var results = await Task.WhenAll(
                PostWebhookAsync(client, json, eventId),
                SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/cancel", customer, new { version = VersionOf(awaiting) }));
            Assert.All(results, r => Assert.True((int)r.StatusCode < 500 || r.StatusCode == HttpStatusCode.ServiceUnavailable, $"{r.StatusCode}: {r.Content.ReadAsStringAsync().Result}"));

            // A webhook that lost the race is retried by the gateway; here, until it is recorded.
            for (var retry = 0; retry < 5 && (await PostWebhookAsync(client, json, eventId)).StatusCode != HttpStatusCode.OK; retry++)
            {
                await Task.Delay(50);
            }

            await using var scope = Factory.Services.CreateAsyncScope();
            var db = DbOf(scope);
            var status = await db.FoodOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync();
            var captured = await db.PaymentAttempts.AsNoTracking().CountAsync(a => a.FoodOrderId == orderId && a.Status == PaymentAttemptStatus.Captured);
            var refunds = await db.Refunds.AsNoTracking().Where(r => r.FoodOrderId == orderId).ToListAsync();
            var ledger = await db.LedgerEntries.AsNoTracking().Where(e => e.FoodOrderId == orderId).ToListAsync();

            // The money is never lost: either the payment confirmed the order (and the cancel lost), or the cancel won and the payment
            // is being refunded in full.
            Assert.Equal(1, captured);
            Assert.Equal(0m, ledger.Sum(e => e.Amount));
            if (status == FoodOrderStatus.Confirmed)
            {
                Assert.Empty(refunds);
                Assert.Contains(ledger, e => e.Account.StartsWith("partner_payable:", StringComparison.Ordinal));
            }
            else
            {
                Assert.Equal(FoodOrderStatus.RefundPending, status);
                var refund = Assert.Single(refunds);
                Assert.Equal((RefundReason.LatePayment, 339.25m), (refund.Reason, refund.Amount));
                Assert.DoesNotContain(ledger, e => e.Account.StartsWith("partner_payable:", StringComparison.Ordinal));
            }
        }
    }

    [DatabaseFact]
    public async Task The_expiry_worker_asks_the_gateway_before_closing_an_unpaid_order()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var paidLate = await AwaitingPaymentOrderAsync(client, "Expiry kitchen");
        var neverPaid = await AwaitingPaymentOrderAsync(client, shop: paidLate.Shop, customer: paidLate.Customer);
        var gatewayOrder = (await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{paidLate.OrderId}/payment", paidLate.Customer))).GetProperty("orderId").GetString()!;
        await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{neverPaid.OrderId}/payment", neverPaid.Customer);
        gateway.CustomerPaid(gatewayOrder, "pay_" + Guid.NewGuid().ToString("N"), 33925);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        await db.Database.ExecuteSqlAsync($"UPDATE food_orders SET expires_at = now() - interval '1 minute' WHERE id IN ({paidLate.OrderId}, {neverPaid.OrderId})");
        await FoodExpiryFor(db, gateway).ExpireDueAsync(default);

        Assert.Equal(FoodOrderStatus.Confirmed, await OrderStatusAsync(paidLate.OrderId));
        Assert.Equal(FoodOrderStatus.Expired, await OrderStatusAsync(neverPaid.OrderId));
        Assert.Equal(
            PaymentAttemptStatus.Expired,
            await db.PaymentAttempts.AsNoTracking().Where(a => a.FoodOrderId == neverPaid.OrderId).Select(a => a.Status).SingleAsync());
    }

    /// <summary>The Worker's food expiry step on a real database, with a fake gateway.</summary>
    private static FoodOrderExpiry FoodExpiryFor(OnePondyDbContext db, FakePaymentGateway gateway)
    {
        var time = TimeProvider.System;
        var payments = Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { Razorpay = new RazorpayOptions { KeyId = "rzp_test_integration" } });
        var food = Microsoft.Extensions.Options.Options.Create(new FoodOptions());
        var ledger = new LedgerService(db, time, payments);
        var refunds = new RefundService(db, gateway, time, payments, NullLogger<RefundService>.Instance);
        var paymentService = new PaymentService(db, gateway, time, payments, ledger, refunds, new NotificationOutbox(db, time), NullLogger<PaymentService>.Instance);
        var orders = new OrderService(db, new RestaurantService(db, new AuditTrail(db, time), time), refunds, food, time);
        return new FoodOrderExpiry(db, orders, paymentService, time, food);
    }

    [DatabaseFact]
    public async Task A_delivered_paid_order_is_settled_with_the_restaurant_through_the_statement_endpoints()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (shop, customer, orderId) = await AwaitingPaymentOrderAsync(client, "Settled kitchen");
        var gatewayOrder = (await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{orderId}/payment", customer))).GetProperty("orderId").GetString()!;
        await PostWebhookAsync(client, PaymentCapturedJson(gatewayOrder, "pay_" + Guid.NewGuid().ToString("N"), 33925), NewEventId());
        var confirmed = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer));
        var preparing = await ReadAsync(await SendAsync(client, HttpMethod.Post, PartnerPath(shop, orderId.ToString(), "preparing"), shop.Owner, new { version = VersionOf(confirmed) }));
        await ReadAsync(await SendAsync(client, HttpMethod.Post, PartnerPath(shop, orderId.ToString(), "ready"), shop.Owner, new { version = VersionOf(preparing) }));

        // Delivered through the domain until the driver app exists (step 8.7); a driver is a real account, as the history requires.
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            var order = await db.FoodOrders.SingleAsync(o => o.Id == orderId);
            var driver = await OwnerOfAsync(db, order.BusinessId);
            order.PickUp(driver, DateTimeOffset.UtcNow);
            db.MarkAdded(order.History[^1]);
            order.Deliver(driver, DateTimeOffset.UtcNow);
            db.MarkAdded(order.History[^1]);
            await db.SaveChangesAsync();
        }

        var admin = await AdministratorTokenAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var drafted = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/admin/settlements", admin, new { businessId = shop.BusinessId, periodStart = today, periodEnd = today }));

        Assert.Equal(266.50m, drafted.GetProperty("settlement").GetProperty("netAmount").GetDecimal());
        var item = drafted.GetProperty("items")[0];
        Assert.Equal(orderId.ToString(), item.GetProperty("foodOrderId").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("bookingId").ValueKind);
        Assert.Equal("Food order", item.GetProperty("listingTitle").GetString());

        var twice = await SendAsync(client, HttpMethod.Post, "/api/v1/admin/settlements", admin, new { businessId = shop.BusinessId, periodStart = today.AddDays(-1), periodEnd = today });
        Assert.Equal("settlement_nothing_due", await ErrorCodeAsync(twice));
    }

    [DatabaseFact]
    public async Task The_database_keeps_food_payments_tied_to_what_they_pay_for()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var paid = await AwaitingPaymentOrderAsync(client, "Strict kitchen");
        var other = await AwaitingPaymentOrderAsync(client, shop: paid.Shop, customer: paid.Customer);
        var gatewayOrder = (await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{paid.OrderId}/payment", paid.Customer))).GetProperty("orderId").GetString()!;
        await PostWebhookAsync(client, PaymentCapturedJson(gatewayOrder, "pay_" + Guid.NewGuid().ToString("N"), 33925), NewEventId());

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var attemptId = await db.PaymentAttempts.AsNoTracking().Where(a => a.FoodOrderId == paid.OrderId).Select(a => a.Id).SingleAsync();
        var businessId = Guid.Parse(paid.Shop.BusinessId);
        await using var sql = new NpgsqlConnection(db.Database.GetConnectionString());
        await sql.OpenAsync();

        async Task<PostgresException> RefusedAsync(string text)
        {
            await using var command = sql.CreateCommand();
            command.CommandText = text;
            command.Parameters.AddWithValue("attempt", attemptId);
            command.Parameters.AddWithValue("paid", paid.OrderId);
            command.Parameters.AddWithValue("other", other.OrderId);
            command.Parameters.AddWithValue("tx", Guid.NewGuid());
            return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }

        // A payment is for exactly one booking or food order, and for good.
        Assert.Equal(
            "ck_payment_attempt_subject",
            (await RefusedAsync("INSERT INTO payment_attempts (id, provider, provider_order_id, amount, currency, status, created_at, updated_at) VALUES (gen_random_uuid(), 'razorpay', 'order_orphan_' || gen_random_uuid(), 10, 'INR', 'created', now(), now())")).ConstraintName);
        Assert.Contains("cannot change what it pays for", (await RefusedAsync("UPDATE payment_attempts SET food_order_id = @other WHERE id = @attempt")).MessageText, StringComparison.Ordinal);

        // A refund gives money back for what its payment paid for, and nothing else (another order, or nothing at all).
        Assert.Contains("is not for what payment", (await RefusedAsync("INSERT INTO refunds (id, payment_attempt_id, food_order_id, amount, reason, status, requested_at) VALUES (gen_random_uuid(), @attempt, @other, 1, 'customer_cancelled', 'pending', now())")).MessageText, StringComparison.Ordinal);
        Assert.Contains("is not for what payment", (await RefusedAsync("INSERT INTO refunds (id, payment_attempt_id, amount, reason, status, requested_at) VALUES (gen_random_uuid(), @attempt, 1, 'customer_cancelled', 'pending', now())")).MessageText, StringComparison.Ordinal);

        // A ledger line is about one thing at most, and only known accounts exist.
        Assert.Equal(
            "ck_ledger_subject",
            (await RefusedAsync("INSERT INTO ledger_entries (id, transaction_id, account, amount, booking_id, food_order_id, description, created_at) VALUES (gen_random_uuid(), @tx, 'tax_payable', 1, gen_random_uuid(), @paid, 'x', now())")).ConstraintName);
        Assert.Equal(
            "ck_ledger_account",
            (await RefusedAsync("INSERT INTO ledger_entries (id, transaction_id, account, amount, food_order_id, description, created_at) VALUES (gen_random_uuid(), @tx, 'tips', 1, @paid, 'x', now())")).ConstraintName);

        // A food order is settled once as a sale.
        var settlement = Settlement.Draft(businessId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), 295m, 28.5m, 0m, DateTimeOffset.UtcNow);
        db.Settlements.Add(settlement);
        db.SettlementItems.Add(SettlementItem.CreateForFoodOrder(settlement.Id, paid.OrderId, SettlementItemKind.Sale, 295m, 28.5m));
        db.SettlementItems.Add(SettlementItem.CreateForFoodOrder(settlement.Id, paid.OrderId, SettlementItemKind.Sale, 295m, 28.5m));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("ux_settlement_items_food_order_kind", ((PostgresException)duplicate.InnerException!).ConstraintName);
    }
}
