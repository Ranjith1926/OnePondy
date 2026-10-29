using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.Payments;

namespace OnePondy.Infrastructure.Payments;

/// <summary>
/// Razorpay's Orders and Refunds APIs (spec D3). Calls are signed with the key pair from <c>Payments:Razorpay</c>;
/// only test keys are accepted while payments are in sandbox (<see cref="PaymentsGuard"/>). Request and response
/// bodies are never logged, only status codes, because they carry customer details.
/// </summary>
public sealed class RazorpayPaymentGateway(HttpClient httpClient, IOptions<PaymentsOptions> options, ILogger<RazorpayPaymentGateway> logger)
    : IPaymentGateway
{
    private readonly RazorpayOptions _options = options.Value.Razorpay;

    public async Task<Result<GatewayOrder>> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken)
    {
        var body = new
        {
            amount = request.AmountPaise,
            currency = request.Currency,
            receipt = request.Receipt,
            notes = new { booking_id = request.BookingId.ToString("D") },
        };

        var response = await PostAsync("orders", body, cancellationToken);
        if (response is null)
        {
            return PaymentErrors.Unavailable;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                LogRejection("order", response);
                return PaymentErrors.Unavailable;
            }

            var order = await ReadAsync<OrderResponse>(response, cancellationToken);
            return order?.Id is { Length: > 0 } id
                ? new GatewayOrder(id, order.Amount, order.Currency ?? request.Currency)
                : PaymentErrors.Unavailable;
        }
    }

    public async Task<Result<GatewayRefund>> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken)
    {
        var body = new { amount = request.AmountPaise, speed = "normal", receipt = request.Receipt };

        // The refund's own id is the idempotency key: if the answer is lost and the call repeated, Razorpay returns the
        // first refund instead of paying the customer twice. The key is 10 or more letters and digits, as Razorpay requires.
        var response = await PostAsync(
            $"payments/{Uri.EscapeDataString(request.PaymentId)}/refund",
            body,
            cancellationToken,
            ("X-Refund-Idempotency", request.Receipt));
        if (response is null)
        {
            return PaymentErrors.Unavailable;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                LogRejection("refund", response);
                return PaymentErrors.Unavailable;
            }

            var refund = await ReadAsync<RefundResponse>(response, cancellationToken);
            return refund?.Id is { Length: > 0 } id
                ? new GatewayRefund(id, refund.Status ?? "pending")
                : PaymentErrors.Unavailable;
        }
    }

    public async Task<Result<IReadOnlyList<GatewayPayment>>> GetOrderPaymentsAsync(string orderId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl.TrimEnd('/')}/orders/{Uri.EscapeDataString(orderId)}/payments");
        request.Headers.Authorization = BasicAuthorization();

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not reach Razorpay");
            return PaymentErrors.Unavailable;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                LogRejection("payment lookup", response);
                return PaymentErrors.Unavailable;
            }

            var page = await ReadAsync<PaymentsResponse>(response, cancellationToken);
            if (page?.Items is null)
            {
                return PaymentErrors.Unavailable;
            }

            IReadOnlyList<GatewayPayment> payments =
            [
                .. page.Items
                    .Where(item => item.Id is { Length: > 0 })
                    .Select(item => new GatewayPayment(item.Id!, item.Amount, item.Currency ?? "INR", item.Status ?? string.Empty)),
            ];
            return Result<IReadOnlyList<GatewayPayment>>.Success(payments);
        }
    }

    /// <summary>Razorpay lists at most 100 payments a page; this many pages is far more than a day of sandbox or early live traffic.</summary>
    private const int PageSize = 100;
    private const int MaxPages = 50;

    public async Task<Result<GatewayPaymentPage>> ListPaymentsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var payments = new List<GatewayPaymentRecord>();
        for (var page = 0; page < MaxPages; page++)
        {
            var path = $"payments?from={from.ToUnixTimeSeconds()}&to={to.ToUnixTimeSeconds()}&count={PageSize}&skip={page * PageSize}";
            var response = await GetAsync(path, cancellationToken);
            if (response is null)
            {
                return PaymentErrors.Unavailable;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    LogRejection("payment list", response);
                    return PaymentErrors.Unavailable;
                }

                var body = await ReadAsync<ListedPaymentsResponse>(response, cancellationToken);
                if (body?.Items is null)
                {
                    return PaymentErrors.Unavailable;
                }

                payments.AddRange(body.Items
                    .Where(item => item.Id is { Length: > 0 })
                    .Select(item => new GatewayPaymentRecord(
                        item.Id!,
                        item.OrderId,
                        item.Amount,
                        item.AmountRefunded,
                        item.Currency ?? "INR",
                        item.Status ?? string.Empty)));

                if (body.Items.Count < PageSize)
                {
                    return new GatewayPaymentPage(payments, Complete: true);
                }
            }
        }

        // Stopped at the page limit with more still to come: say so rather than let "not listed" look like "missing".
        return new GatewayPaymentPage(payments, Complete: false);
    }

    public async Task<Result<GatewayRefundDetail>> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        var response = await GetAsync($"refunds/{Uri.EscapeDataString(refundId)}", cancellationToken);
        if (response is null)
        {
            return PaymentErrors.Unavailable;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                LogRejection("refund lookup", response);
                return PaymentErrors.Unavailable;
            }

            var refund = await ReadAsync<RefundDetailResponse>(response, cancellationToken);
            return refund?.Id is { Length: > 0 } id
                ? new GatewayRefundDetail(id, refund.PaymentId, refund.Amount, refund.Status ?? string.Empty)
                : PaymentErrors.Unavailable;
        }
    }

    private async Task<HttpResponseMessage?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl.TrimEnd('/')}/{path}");
        request.Headers.Authorization = BasicAuthorization();
        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not reach Razorpay");
            return null;
        }
    }

    private AuthenticationHeaderValue BasicAuthorization() => new(
        "Basic",
        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.KeyId}:{_options.KeySecret}")));

    private async Task<HttpResponseMessage?> PostAsync(
        string path,
        object body,
        CancellationToken cancellationToken,
        (string Name, string Value)? header = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/{path}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = BasicAuthorization();
        if (header is { } extra)
        {
            request.Headers.Add(extra.Name, extra.Value);
        }

        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not reach Razorpay");
            return null;
        }
    }

    private void LogRejection(string what, HttpResponseMessage response)
    {
        // 401 means the key pair is wrong or not for this mode, which an operator must fix; anything else may be transient.
        if ((int)response.StatusCode == 401)
        {
            logger.LogError("Razorpay refused the {What} request as unauthorised: check Payments:Razorpay:KeyId and KeySecret", what);
        }
        else
        {
            logger.LogWarning("Razorpay refused the {What} request with status {StatusCode}", what, (int)response.StatusCode);
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private sealed record OrderResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("currency")] string? Currency);

    private sealed record PaymentsResponse([property: JsonPropertyName("items")] List<PaymentItem>? Items);

    private sealed record PaymentItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("status")] string? Status);

    private sealed record ListedPaymentsResponse([property: JsonPropertyName("items")] List<ListedPayment>? Items);

    private sealed record ListedPayment(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("order_id")] string? OrderId,
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("amount_refunded")] long AmountRefunded,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("status")] string? Status);

    private sealed record RefundDetailResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("payment_id")] string? PaymentId,
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("status")] string? Status);

    private sealed record RefundResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("status")] string? Status);
}

/// <summary>Used until Razorpay keys are configured: every call reports online payment as unavailable.</summary>
public sealed class UnavailablePaymentGateway : IPaymentGateway
{
    public Task<Result<GatewayOrder>> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<GatewayOrder>.Failure(PaymentErrors.Unavailable));

    public Task<Result<GatewayRefund>> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<GatewayRefund>.Failure(PaymentErrors.Unavailable));

    public Task<Result<IReadOnlyList<GatewayPayment>>> GetOrderPaymentsAsync(string orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<GatewayPayment>>.Failure(PaymentErrors.Unavailable));

    public Task<Result<GatewayPaymentPage>> ListPaymentsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult(Result<GatewayPaymentPage>.Failure(PaymentErrors.Unavailable));

    public Task<Result<GatewayRefundDetail>> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
        Task.FromResult(Result<GatewayRefundDetail>.Failure(PaymentErrors.Unavailable));
}
