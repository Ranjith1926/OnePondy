namespace OnePondy.Domain.Payments;

/// <summary>What kind of difference between our records and the payment gateway's was found (spec Phase 7: reconciliation).</summary>
public enum ReconciliationKind
{
    /// <summary>A webhook was stored but could not be applied (unknown order, wrong amount, unknown refund).</summary>
    WebhookNeedsAttention = 1,

    /// <summary>A webhook was stored but never finished, and has not been redelivered.</summary>
    WebhookUnprocessed = 2,

    /// <summary>A payment was found at the gateway that no webhook told us about; it was applied. Recorded so a lost-webhook problem is visible.</summary>
    PaymentAppliedWithoutWebhook = 3,

    /// <summary>The gateway holds a captured payment for an order we do not know.</summary>
    GatewayPaymentUnknown = 4,

    /// <summary>We recorded a captured payment that the gateway does not show.</summary>
    PaymentMissingAtGateway = 5,

    /// <summary>The gateway and we disagree on the amount or the payment id for an order.</summary>
    PaymentMismatch = 6,

    /// <summary>A refund the gateway reported paid, with no webhook; it was applied. Recorded for visibility.</summary>
    RefundAppliedWithoutWebhook = 7,

    /// <summary>A refund was recorded but never reached the gateway.</summary>
    RefundNotSent = 8,

    /// <summary>A refund has been waiting at the gateway for too long.</summary>
    RefundStuck = 9,

    /// <summary>The gateway reports a refund failed; the customer has not been paid back.</summary>
    RefundFailed = 10,

    /// <summary>The refunds the gateway shows for a payment do not add up to ours.</summary>
    RefundTotalMismatch = 11,

    /// <summary>A payment or refund has no ledger entries, or the ledger does not add up.</summary>
    LedgerProblem = 12,
}

public enum ReconciliationStatus
{
    /// <summary>Needs a person to look at it.</summary>
    Open = 1,

    /// <summary>A person looked at it and recorded what was done.</summary>
    Resolved = 2,

    /// <summary>The system found and fixed it by the same safe path a webhook takes, and recorded it.</summary>
    AutoResolved = 3,
}

/// <summary>
/// One difference found between our records and the gateway's. Differences are listed for a person, never fixed
/// silently: the few the system does fix (a payment whose webhook was lost) are applied by the same path a webhook
/// takes and recorded here as <see cref="ReconciliationStatus.AutoResolved"/>. Each problem is recorded once, by its
/// <see cref="Fingerprint"/>, however many times a check sees it.
/// </summary>
public sealed class ReconciliationException
{
    public const int MaxDetailLength = 500;
    public const int MaxNoteLength = 500;

    private ReconciliationException()
    {
    }

    public Guid Id { get; private set; }

    public ReconciliationKind Kind { get; private set; }

    public ReconciliationStatus Status { get; private set; }

    /// <summary>Identifies the problem, for example <c>refund_stuck:{refund id}</c>, so a repeat of it is recognised.</summary>
    public string Fingerprint { get; private set; } = string.Empty;

    public Guid? BookingId { get; private set; }

    public Guid? PaymentAttemptId { get; private set; }

    public Guid? RefundId { get; private set; }

    public Guid? GatewayEventId { get; private set; }

    /// <summary>The gateway's own id for the payment, order or refund concerned.</summary>
    public string? ProviderReference { get; private set; }

    public decimal? ExpectedAmount { get; private set; }

    public decimal? ActualAmount { get; private set; }

    public string Detail { get; private set; } = string.Empty;

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>How many times a check has seen this problem.</summary>
    public int Occurrences { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public string? ResolutionNote { get; private set; }

    public static ReconciliationException Detect(
        ReconciliationKind kind,
        string fingerprint,
        string detail,
        DateTimeOffset now,
        Guid? bookingId = null,
        Guid? paymentAttemptId = null,
        Guid? refundId = null,
        Guid? gatewayEventId = null,
        string? providerReference = null,
        decimal? expectedAmount = null,
        decimal? actualAmount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        return new ReconciliationException
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            Status = ReconciliationStatus.Open,
            Fingerprint = fingerprint,
            BookingId = bookingId,
            PaymentAttemptId = paymentAttemptId,
            RefundId = refundId,
            GatewayEventId = gatewayEventId,
            ProviderReference = providerReference,
            ExpectedAmount = expectedAmount,
            ActualAmount = actualAmount,
            Detail = Truncate(detail, MaxDetailLength),
            FirstSeenAt = now,
            LastSeenAt = now,
            Occurrences = 1,
        };
    }

    /// <summary>A check saw the same problem again.</summary>
    public void SeenAgain(DateTimeOffset now)
    {
        LastSeenAt = now;
        Occurrences++;
    }

    /// <summary>A person has dealt with it and says what they did.</summary>
    public void Resolve(Guid userId, string note, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        if (Status != ReconciliationStatus.Open)
        {
            throw new InvalidOperationException("Only an open exception can be resolved.");
        }

        Status = ReconciliationStatus.Resolved;
        ResolvedAt = now;
        ResolvedByUserId = userId;
        ResolutionNote = Truncate(note.Trim(), MaxNoteLength);
    }

    /// <summary>The system fixed it by the same safe path a webhook takes, and records what it did.</summary>
    public void AutoResolve(string note, DateTimeOffset now)
    {
        if (Status != ReconciliationStatus.Open)
        {
            throw new InvalidOperationException("Only an open exception can be resolved.");
        }

        Status = ReconciliationStatus.AutoResolved;
        ResolvedAt = now;
        ResolutionNote = Truncate(note, MaxNoteLength);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
