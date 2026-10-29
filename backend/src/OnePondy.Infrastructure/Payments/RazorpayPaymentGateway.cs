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

        var response = await PostAsync($"payments/{Uri.EscapeDataString(request.PaymentId)}/refund", body, cancellationToken);
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

    private async Task<HttpResponseMessage?> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/{path}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.KeyId}:{_options.KeySecret}")));

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
}
