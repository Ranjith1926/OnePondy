using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Payments;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Migration 3 (spec section 17.3, Phase 7). Money is numeric(12,2) in INR. The balance, over-refund and
// append-only rules that EF cannot express are triggers added by hand in the AddPaymentsAndLedger migration.

public sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts", table =>
        {
            table.HasCheckConstraint("ck_payment_attempt_provider", $"provider IN ({SnakeCaseEnum<PaymentProvider>.SqlList})");
            table.HasCheckConstraint("ck_payment_attempt_status", $"status IN ({SnakeCaseEnum<PaymentAttemptStatus>.SqlList})");
            table.HasCheckConstraint("ck_payment_attempt_amount", "amount > 0");
            table.HasCheckConstraint(
                "ck_payment_attempt_captured",
                $"status <> '{SnakeCaseEnum<PaymentAttemptStatus>.ToDatabase(PaymentAttemptStatus.Captured)}' OR provider_payment_id IS NOT NULL");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Provider).HasConversion(SnakeCaseEnum<PaymentProvider>.Converter).HasMaxLength(16);
        builder.Property(p => p.ProviderOrderId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(64);
        builder.Property(p => p.Amount).HasPrecision(12, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(p => p.Status).HasConversion(SnakeCaseEnum<PaymentAttemptStatus>.Converter).HasMaxLength(16);
        builder.Property(p => p.Version).IsRowVersion();

        builder.HasOne<Booking>().WithMany().HasForeignKey(p => p.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.ProviderOrderId).IsUnique().HasDatabaseName("ux_payment_attempts_order");
        builder.HasIndex(p => p.ProviderPaymentId).IsUnique().HasDatabaseName("ux_payment_attempts_payment").HasFilter("provider_payment_id IS NOT NULL");
        builder.HasIndex(p => new { p.BookingId, p.CreatedAt }).HasDatabaseName("ix_payment_attempts_booking");
        builder.HasIndex(p => p.CreatedAt)
            .HasDatabaseName("ix_payment_attempts_open")
            .HasFilter($"status = '{SnakeCaseEnum<PaymentAttemptStatus>.ToDatabase(PaymentAttemptStatus.Created)}'");

        // At most one open attempt per booking, so two requests at the same instant cannot offer two ways to pay.
        builder.HasIndex(p => p.BookingId)
            .IsUnique()
            .HasDatabaseName("ux_payment_attempts_open_booking")
            .HasFilter($"status = '{SnakeCaseEnum<PaymentAttemptStatus>.ToDatabase(PaymentAttemptStatus.Created)}'");
    }
}

public sealed class GatewayEventConfiguration : IEntityTypeConfiguration<GatewayEvent>
{
    public void Configure(EntityTypeBuilder<GatewayEvent> builder)
    {
        builder.ToTable("gateway_events", table =>
            table.HasCheckConstraint("ck_gateway_event_provider", $"provider IN ({SnakeCaseEnum<PaymentProvider>.SqlList})"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Provider).HasConversion(SnakeCaseEnum<PaymentProvider>.Converter).HasMaxLength(16);
        builder.Property(e => e.ProviderEventId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Error).HasMaxLength(500);

        // A provider that retries a delivery sends the same event id: the second insert fails and is ignored.
        builder.HasIndex(e => new { e.Provider, e.ProviderEventId }).IsUnique().HasDatabaseName("ux_gateway_events_event");
        builder.HasIndex(e => e.ReceivedAt).HasDatabaseName("ix_gateway_events_unprocessed").HasFilter("processed_at IS NULL");
    }
}

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds", table =>
        {
            table.HasCheckConstraint("ck_refund_amount", "amount > 0");
            table.HasCheckConstraint("ck_refund_reason", $"reason IN ({SnakeCaseEnum<RefundReason>.SqlList})");
            table.HasCheckConstraint("ck_refund_status", $"status IN ({SnakeCaseEnum<RefundStatus>.SqlList})");
            table.HasCheckConstraint(
                "ck_refund_processed",
                $"status <> '{SnakeCaseEnum<RefundStatus>.ToDatabase(RefundStatus.Processed)}' OR provider_refund_id IS NOT NULL");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Amount).HasPrecision(12, 2);
        builder.Property(r => r.Reason).HasConversion(SnakeCaseEnum<RefundReason>.Converter).HasMaxLength(24);
        builder.Property(r => r.Status).HasConversion(SnakeCaseEnum<RefundStatus>.Converter).HasMaxLength(16);
        builder.Property(r => r.ProviderRefundId).HasMaxLength(64);

        builder.HasOne<PaymentAttempt>().WithMany().HasForeignKey(r => r.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Booking>().WithMany().HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.PaymentAttemptId).HasDatabaseName("ix_refunds_payment_attempt");
        builder.HasIndex(r => r.BookingId).HasDatabaseName("ix_refunds_booking");
        builder.HasIndex(r => r.ProviderRefundId).IsUnique().HasDatabaseName("ux_refunds_provider_refund").HasFilter("provider_refund_id IS NOT NULL");
    }
}

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries", table =>
        {
            table.HasCheckConstraint("ck_ledger_amount", "amount <> 0");
            table.HasCheckConstraint(
                "ck_ledger_account",
                "account ~ '^(gateway_clearing|customer_refunds|platform_commission|(partner_payable|partner_paid):[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$'");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Account).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(12, 2);
        builder.Property(e => e.Description).HasMaxLength(200).IsRequired();

        builder.HasOne<Booking>().WithMany().HasForeignKey(e => e.BookingId).OnDelete(DeleteBehavior.Restrict);
        // One line per account in a transaction: posting the same event twice cannot double count.
        builder.HasIndex(e => new { e.TransactionId, e.Account }).IsUnique().HasDatabaseName("ux_ledger_entries_transaction_account");
        builder.HasIndex(e => new { e.Account, e.CreatedAt }).HasDatabaseName("ix_ledger_entries_account");
        builder.HasIndex(e => e.BookingId).HasDatabaseName("ix_ledger_entries_booking");
    }
}

public sealed class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> builder)
    {
        builder.ToTable("settlements", table =>
        {
            table.HasCheckConstraint("ck_settlement_status", $"status IN ({SnakeCaseEnum<SettlementStatus>.SqlList})");
            table.HasCheckConstraint("ck_settlement_period", "period_end >= period_start");
            table.HasCheckConstraint("ck_settlement_amounts", "gross_amount >= 0 AND commission_amount >= 0 AND refund_amount >= 0");
            table.HasCheckConstraint("ck_settlement_net", "net_amount = gross_amount - commission_amount - refund_amount");
            table.HasCheckConstraint(
                "ck_settlement_paid",
                $"status <> '{SnakeCaseEnum<SettlementStatus>.ToDatabase(SettlementStatus.Paid)}' OR (payout_reference IS NOT NULL AND paid_at IS NOT NULL)");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.GrossAmount).HasPrecision(12, 2);
        builder.Property(s => s.CommissionAmount).HasPrecision(12, 2);
        builder.Property(s => s.RefundAmount).HasPrecision(12, 2);
        builder.Property(s => s.NetAmount).HasPrecision(12, 2);
        builder.Property(s => s.Status).HasConversion(SnakeCaseEnum<SettlementStatus>.Converter).HasMaxLength(16);
        builder.Property(s => s.PayoutReference).HasMaxLength(100);

        builder.HasOne<Business>().WithMany().HasForeignKey(s => s.BusinessId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.BusinessId, s.PeriodStart, s.PeriodEnd }).IsUnique().HasDatabaseName("ux_settlements_period");
    }
}

public sealed class SettlementItemConfiguration : IEntityTypeConfiguration<SettlementItem>
{
    public void Configure(EntityTypeBuilder<SettlementItem> builder)
    {
        builder.ToTable("settlement_items", table =>
        {
            table.HasCheckConstraint("ck_settlement_item_kind", $"kind IN ({SnakeCaseEnum<SettlementItemKind>.SqlList})");
            table.HasCheckConstraint("ck_settlement_item_amounts", "gross_amount >= 0 AND commission_amount >= 0");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Kind).HasConversion(SnakeCaseEnum<SettlementItemKind>.Converter).HasMaxLength(16);
        builder.Property(i => i.GrossAmount).HasPrecision(12, 2);
        builder.Property(i => i.CommissionAmount).HasPrecision(12, 2);

        builder.HasOne<Settlement>().WithMany().HasForeignKey(i => i.SettlementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Booking>().WithMany().HasForeignKey(i => i.BookingId).OnDelete(DeleteBehavior.Restrict);

        // A booking is settled once as a sale and at most once as a refund, so no statement double-counts it.
        builder.HasIndex(i => new { i.BookingId, i.Kind }).IsUnique().HasDatabaseName("ux_settlement_items_booking_kind");
        builder.HasIndex(i => i.SettlementId).HasDatabaseName("ix_settlement_items_settlement");
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", table => table.HasCheckConstraint("ck_outbox_attempts", "attempts >= 0"));

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(500);

        builder.HasIndex(m => m.CreatedAt).HasDatabaseName("ix_outbox_pending").HasFilter("dispatched_at IS NULL");
    }
}

public sealed class ReconciliationExceptionConfiguration : IEntityTypeConfiguration<ReconciliationException>
{
    public void Configure(EntityTypeBuilder<ReconciliationException> builder)
    {
        builder.ToTable("reconciliation_exceptions", table =>
        {
            table.HasCheckConstraint("ck_reconciliation_kind", $"kind IN ({SnakeCaseEnum<ReconciliationKind>.SqlList})");
            table.HasCheckConstraint("ck_reconciliation_status", $"status IN ({SnakeCaseEnum<ReconciliationStatus>.SqlList})");
            // Open exactly while nobody has dealt with it.
            table.HasCheckConstraint(
                "ck_reconciliation_resolved",
                $"(status = '{SnakeCaseEnum<ReconciliationStatus>.ToDatabase(ReconciliationStatus.Open)}') = (resolved_at IS NULL)");
            table.HasCheckConstraint(
                "ck_reconciliation_person_note",
                $"status <> '{SnakeCaseEnum<ReconciliationStatus>.ToDatabase(ReconciliationStatus.Resolved)}' OR (resolved_by_user_id IS NOT NULL AND resolution_note IS NOT NULL)");
            table.HasCheckConstraint("ck_reconciliation_occurrences", "occurrences >= 1");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion(SnakeCaseEnum<ReconciliationKind>.Converter).HasMaxLength(40);
        builder.Property(e => e.Status).HasConversion(SnakeCaseEnum<ReconciliationStatus>.Converter).HasMaxLength(16);
        builder.Property(e => e.Fingerprint).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ProviderReference).HasMaxLength(100);
        builder.Property(e => e.ExpectedAmount).HasPrecision(12, 2);
        builder.Property(e => e.ActualAmount).HasPrecision(12, 2);
        builder.Property(e => e.Detail).HasMaxLength(ReconciliationException.MaxDetailLength).IsRequired();
        builder.Property(e => e.ResolutionNote).HasMaxLength(ReconciliationException.MaxNoteLength);

        builder.HasOne<Booking>().WithMany().HasForeignKey(e => e.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentAttempt>().WithMany().HasForeignKey(e => e.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Refund>().WithMany().HasForeignKey(e => e.RefundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GatewayEvent>().WithMany().HasForeignKey(e => e.GatewayEventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Each problem is recorded once, whatever its status, so a resolved one is never re-opened and a repeat is only counted.
        builder.HasIndex(e => e.Fingerprint).IsUnique().HasDatabaseName("ux_reconciliation_fingerprint");
        builder.HasIndex(e => new { e.Status, e.LastSeenAt }).IsDescending(false, true).HasDatabaseName("ix_reconciliation_status_seen");
    }
}
