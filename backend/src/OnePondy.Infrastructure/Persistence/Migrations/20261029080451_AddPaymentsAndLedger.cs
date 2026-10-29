using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentsAndLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gateway_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gateway_events", x => x.id);
                    table.CheckConstraint("ck_gateway_event_provider", "provider IN ('razorpay')");
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_account", "account ~ '^(gateway_clearing|customer_refunds|platform_commission|(partner_payable|partner_paid):[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$'");
                    table.CheckConstraint("ck_ledger_amount", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_ledger_entries_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_attempts", "attempts >= 0");
                });

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_payment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_attempts", x => x.id);
                    table.CheckConstraint("ck_payment_attempt_amount", "amount > 0");
                    table.CheckConstraint("ck_payment_attempt_captured", "status <> 'captured' OR provider_payment_id IS NOT NULL");
                    table.CheckConstraint("ck_payment_attempt_provider", "provider IN ('razorpay')");
                    table.CheckConstraint("ck_payment_attempt_status", "status IN ('created', 'captured', 'failed', 'expired')");
                    table.ForeignKey(
                        name: "fk_payment_attempts_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    commission_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    refund_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payout_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlements", x => x.id);
                    table.CheckConstraint("ck_settlement_amounts", "gross_amount >= 0 AND commission_amount >= 0 AND refund_amount >= 0");
                    table.CheckConstraint("ck_settlement_net", "net_amount = gross_amount - commission_amount - refund_amount");
                    table.CheckConstraint("ck_settlement_paid", "status <> 'paid' OR (payout_reference IS NOT NULL AND paid_at IS NOT NULL)");
                    table.CheckConstraint("ck_settlement_period", "period_end >= period_start");
                    table.CheckConstraint("ck_settlement_status", "status IN ('draft', 'approved', 'paid')");
                    table.ForeignKey(
                        name: "fk_settlements_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_refund_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.CheckConstraint("ck_refund_amount", "amount > 0");
                    table.CheckConstraint("ck_refund_processed", "status <> 'processed' OR provider_refund_id IS NOT NULL");
                    table.CheckConstraint("ck_refund_reason", "reason IN ('rejected', 'partner_cancelled', 'customer_cancelled', 'late_payment')");
                    table.CheckConstraint("ck_refund_status", "status IN ('pending', 'processed', 'failed')");
                    table.ForeignKey(
                        name: "fk_refunds_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refunds_payment_attempts_payment_attempt_id",
                        column: x => x.payment_attempt_id,
                        principalTable: "payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlement_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    commission_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlement_items", x => x.id);
                    table.CheckConstraint("ck_settlement_item_amounts", "gross_amount >= 0 AND commission_amount >= 0");
                    table.CheckConstraint("ck_settlement_item_kind", "kind IN ('sale', 'refund')");
                    table.ForeignKey(
                        name: "fk_settlement_items_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_items_settlements_settlement_id",
                        column: x => x.settlement_id,
                        principalTable: "settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "module_flags",
                columns: new[] { "key", "status", "updated_at", "updated_by_user_id" },
                values: new object[] { "payments", "coming_soon", null, null });

            migrationBuilder.CreateIndex(
                name: "ix_gateway_events_unprocessed",
                table: "gateway_events",
                column: "received_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_gateway_events_event",
                table: "gateway_events",
                columns: new[] { "provider", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account",
                table: "ledger_entries",
                columns: new[] { "account", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_booking",
                table: "ledger_entries",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transaction",
                table: "ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                table: "outbox_messages",
                column: "created_at",
                filter: "dispatched_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_booking",
                table: "payment_attempts",
                columns: new[] { "booking_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_open",
                table: "payment_attempts",
                column: "created_at",
                filter: "status = 'created'");

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_order",
                table: "payment_attempts",
                column: "provider_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_payment",
                table: "payment_attempts",
                column: "provider_payment_id",
                unique: true,
                filter: "provider_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_booking",
                table: "refunds",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_payment_attempt",
                table: "refunds",
                column: "payment_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ux_refunds_provider_refund",
                table: "refunds",
                column: "provider_refund_id",
                unique: true,
                filter: "provider_refund_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_items_settlement",
                table: "settlement_items",
                column: "settlement_id");

            migrationBuilder.CreateIndex(
                name: "ux_settlement_items_booking_kind",
                table: "settlement_items",
                columns: new[] { "booking_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_settlements_period",
                table: "settlements",
                columns: new[] { "business_id", "period_start", "period_end" },
                unique: true);

            // Rules EF cannot express (spec section 17.3). Every ledger transaction must sum to zero when it
            // commits, ledger lines are never changed or removed, and refunds can never exceed what was captured.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION ledger_entries_must_balance() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE total numeric;
                BEGIN
                    SELECT COALESCE(SUM(amount), 0) INTO total FROM ledger_entries WHERE transaction_id = NEW.transaction_id;
                    IF total <> 0 THEN
                        RAISE EXCEPTION 'ledger transaction % does not balance (off by %)', NEW.transaction_id, total;
                    END IF;
                    RETURN NULL;
                END;
                $$;

                CREATE CONSTRAINT TRIGGER trg_ledger_entries_balance
                    AFTER INSERT ON ledger_entries
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION ledger_entries_must_balance();

                CREATE FUNCTION ledger_entries_is_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'ledger_entries is append-only';
                END;
                $$;

                CREATE TRIGGER trg_ledger_entries_append_only BEFORE UPDATE OR DELETE ON ledger_entries
                    FOR EACH ROW EXECUTE FUNCTION ledger_entries_is_append_only();
                CREATE TRIGGER trg_ledger_entries_no_truncate BEFORE TRUNCATE ON ledger_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION ledger_entries_is_append_only();

                CREATE FUNCTION refunds_within_captured_amount() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE captured numeric; already numeric;
                BEGIN
                    IF NEW.status = 'failed' THEN
                        RETURN NEW;
                    END IF;
                    -- Lock the payment so two refunds requested at once cannot both pass the check.
                    SELECT amount INTO captured FROM payment_attempts WHERE id = NEW.payment_attempt_id FOR UPDATE;
                    SELECT COALESCE(SUM(amount), 0) INTO already FROM refunds
                        WHERE payment_attempt_id = NEW.payment_attempt_id AND status <> 'failed' AND id <> NEW.id;
                    IF already + NEW.amount > captured THEN
                        RAISE EXCEPTION 'refunds of % would exceed the % paid', already + NEW.amount, captured;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_refunds_within_captured BEFORE INSERT OR UPDATE ON refunds
                    FOR EACH ROW EXECUTE FUNCTION refunds_within_captured_amount();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gateway_events");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropTable(
                name: "settlement_items");

            migrationBuilder.DropTable(
                name: "payment_attempts");

            migrationBuilder.DropTable(
                name: "settlements");

            migrationBuilder.DeleteData(
                table: "module_flags",
                keyColumn: "key",
                keyValue: "payments");

            // The tables are gone, and their triggers with them; remove the functions they used.
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS ledger_entries_must_balance();
                DROP FUNCTION IF EXISTS ledger_entries_is_append_only();
                DROP FUNCTION IF EXISTS refunds_within_captured_amount();
                """);
        }
    }
}
