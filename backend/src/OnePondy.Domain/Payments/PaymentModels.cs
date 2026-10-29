namespace OnePondy.Domain.Payments;

public enum PaymentProvider
{
    Razorpay = 1,
}

public enum PaymentAttemptStatus
{
    Created = 1,
    Captured = 2,
    Failed = 3,
    Expired = 4,
}

/// <summary>Why money goes back to a customer (spec D22).</summary>
public enum RefundReason
{
    Rejected = 1,
    PartnerCancelled = 2,
    CustomerCancelled = 3,
    LatePayment = 4,
}

public enum RefundStatus
{
    Pending = 1,
    Processed = 2,
    Failed = 3,
}

public enum SettlementStatus
{
    Draft = 1,
    Approved = 2,
    Paid = 3,
}

public enum SettlementItemKind
{
    Sale = 1,
    Refund = 2,
}

/// <summary>One try at paying for a booking: a gateway order, and what became of it.</summary>
public sealed class PaymentAttempt
{
    private PaymentAttempt()
    {
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public PaymentProvider Provider { get; private set; }

    public string ProviderOrderId { get; private set; } = string.Empty;

    public string? ProviderPaymentId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "INR";

    public PaymentAttemptStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public static PaymentAttempt Create(Guid bookingId, string providerOrderId, decimal amount, string currency, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerOrderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        return new PaymentAttempt
        {
            Id = Guid.CreateVersion7(),
            BookingId = bookingId,
            Provider = PaymentProvider.Razorpay,
            ProviderOrderId = providerOrderId,
            Amount = amount,
            Currency = currency,
            Status = PaymentAttemptStatus.Created,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Records that the gateway took the money. A capture is a fact, so it is accepted whatever we thought the
    /// attempt's state was (a customer can still pay an order we had already given up on); it is not accepted twice.
    /// </summary>
    public void MarkCaptured(string providerPaymentId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPaymentId);
        if (Status == PaymentAttemptStatus.Captured)
        {
            throw new InvalidOperationException("This payment attempt was already captured.");
        }

        ProviderPaymentId = providerPaymentId;
        Status = PaymentAttemptStatus.Captured;
        UpdatedAt = now;
    }

    public void MarkFailed(DateTimeOffset now) => Close(PaymentAttemptStatus.Failed, now);

    public void MarkExpired(DateTimeOffset now) => Close(PaymentAttemptStatus.Expired, now);

    private void Close(PaymentAttemptStatus next, DateTimeOffset now)
    {
        if (Status != PaymentAttemptStatus.Created)
        {
            throw new InvalidOperationException($"A {Status} payment attempt cannot become {next}.");
        }

        Status = next;
        UpdatedAt = now;
    }
}

/// <summary>A webhook as received. The provider's event id is unique, so a replay is recognised and ignored.</summary>
public sealed class GatewayEvent
{
    private GatewayEvent()
    {
    }

    public Guid Id { get; private set; }

    public PaymentProvider Provider { get; private set; }

    public string ProviderEventId { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    /// <summary>The raw JSON body, kept exactly as received.</summary>
    public string Payload { get; private set; } = "{}";

    public bool SignatureValid { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? Error { get; private set; }

    public static GatewayEvent Receive(
        string providerEventId,
        string eventType,
        string payload,
        bool signatureValid,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);
        return new GatewayEvent
        {
            Id = Guid.CreateVersion7(),
            Provider = PaymentProvider.Razorpay,
            ProviderEventId = providerEventId,
            EventType = eventType,
            Payload = payload,
            SignatureValid = signatureValid,
            ReceivedAt = now,
        };
    }

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt = now;

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Error = error.Length > 500 ? error[..500] : error;
        ProcessedAt = now;
    }
}

public sealed class Refund
{
    private Refund()
    {
    }

    public Guid Id { get; private set; }

    public Guid PaymentAttemptId { get; private set; }

    public Guid BookingId { get; private set; }

    public decimal Amount { get; private set; }

    public RefundReason Reason { get; private set; }

    public RefundStatus Status { get; private set; }

    public string? ProviderRefundId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public static Refund Request(Guid paymentAttemptId, Guid bookingId, decimal amount, RefundReason reason, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        return new Refund
        {
            Id = Guid.CreateVersion7(),
            PaymentAttemptId = paymentAttemptId,
            BookingId = bookingId,
            Amount = amount,
            Reason = reason,
            Status = RefundStatus.Pending,
            RequestedAt = now,
        };
    }

    /// <summary>The gateway accepted the refund; it stays pending until the gateway reports it processed.</summary>
    public void MarkInitiated(string providerRefundId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerRefundId);
        ProviderRefundId = providerRefundId;
    }

    public void MarkProcessed(string providerRefundId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerRefundId);
        ProviderRefundId = providerRefundId;
        Status = RefundStatus.Processed;
        ProcessedAt = now;
    }

    public void MarkFailed(DateTimeOffset now)
    {
        Status = RefundStatus.Failed;
        ProcessedAt = now;
    }
}

/// <summary>Names of the ledger accounts. A partner's accounts are per business.</summary>
public static class LedgerAccounts
{
    public const string GatewayClearing = "gateway_clearing";
    public const string CustomerRefunds = "customer_refunds";
    public const string PlatformCommission = "platform_commission";

    public static string PartnerPayable(Guid businessId) => $"partner_payable:{businessId}";

    public static string PartnerPaid(Guid businessId) => $"partner_paid:{businessId}";
}

/// <summary>
/// One line of a double-entry transaction. Every <see cref="TransactionId"/> must sum to zero, which the
/// database enforces when the transaction commits. Lines are never changed or removed.
/// </summary>
public sealed class LedgerEntry
{
    private LedgerEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid TransactionId { get; private set; }

    public string Account { get; private set; } = string.Empty;

    /// <summary>Signed: positive money comes into the account, negative leaves it.</summary>
    public decimal Amount { get; private set; }

    public Guid? BookingId { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static LedgerEntry Create(Guid transactionId, string account, decimal amount, Guid? bookingId, string description, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        if (amount == 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A ledger line cannot be zero.");
        }

        return new LedgerEntry
        {
            Id = Guid.CreateVersion7(),
            TransactionId = transactionId,
            Account = account,
            Amount = amount,
            BookingId = bookingId,
            Description = description,
            CreatedAt = now,
        };
    }
}

/// <summary>A statement of what one business is owed for a period.</summary>
public sealed class Settlement
{
    private Settlement()
    {
    }

    public Guid Id { get; private set; }

    public Guid BusinessId { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public decimal GrossAmount { get; private set; }

    public decimal CommissionAmount { get; private set; }

    public decimal RefundAmount { get; private set; }

    public decimal NetAmount { get; private set; }

    public SettlementStatus Status { get; private set; }

    public string? PayoutReference { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public static Settlement Draft(
        Guid businessId,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal grossAmount,
        decimal commissionAmount,
        decimal refundAmount,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            GrossAmount = grossAmount,
            CommissionAmount = commissionAmount,
            RefundAmount = refundAmount,
            NetAmount = grossAmount - commissionAmount - refundAmount,
            Status = SettlementStatus.Draft,
            CreatedAt = now,
        };
}

public sealed class SettlementItem
{
    private SettlementItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid SettlementId { get; private set; }

    public Guid BookingId { get; private set; }

    public SettlementItemKind Kind { get; private set; }

    public decimal GrossAmount { get; private set; }

    public decimal CommissionAmount { get; private set; }

    public static SettlementItem Create(Guid settlementId, Guid bookingId, SettlementItemKind kind, decimal grossAmount, decimal commissionAmount) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            SettlementId = settlementId,
            BookingId = bookingId,
            Kind = kind,
            GrossAmount = grossAmount,
            CommissionAmount = commissionAmount,
        };
}

/// <summary>
/// A domain event written in the same transaction as the change that caused it; the Worker delivers it
/// afterwards (spec section 10), so a status change and its notification either both happen or neither does.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = "{}";

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(string type, string payload, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return new OutboxMessage { Id = Guid.CreateVersion7(), Type = type, Payload = payload, CreatedAt = now };
    }

    public void MarkDispatched(DateTimeOffset now) => DispatchedAt = now;

    public void MarkAttemptFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
    }
}
