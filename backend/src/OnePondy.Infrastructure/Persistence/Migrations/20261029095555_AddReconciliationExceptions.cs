using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reconciliation_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    refund_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gateway_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expected_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    actual_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_exceptions", x => x.id);
                    table.CheckConstraint("ck_reconciliation_kind", "kind IN ('webhook_needs_attention', 'webhook_unprocessed', 'payment_applied_without_webhook', 'gateway_payment_unknown', 'payment_missing_at_gateway', 'payment_mismatch', 'refund_applied_without_webhook', 'refund_not_sent', 'refund_stuck', 'refund_failed', 'refund_total_mismatch', 'ledger_problem')");
                    table.CheckConstraint("ck_reconciliation_occurrences", "occurrences >= 1");
                    table.CheckConstraint("ck_reconciliation_person_note", "status <> 'resolved' OR (resolved_by_user_id IS NOT NULL AND resolution_note IS NOT NULL)");
                    table.CheckConstraint("ck_reconciliation_resolved", "(status = 'open') = (resolved_at IS NULL)");
                    table.CheckConstraint("ck_reconciliation_status", "status IN ('open', 'resolved', 'auto_resolved')");
                    table.ForeignKey(
                        name: "fk_reconciliation_exceptions_app_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reconciliation_exceptions_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reconciliation_exceptions_gateway_events_gateway_event_id",
                        column: x => x.gateway_event_id,
                        principalTable: "gateway_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reconciliation_exceptions_payment_attempts_payment_attempt_",
                        column: x => x.payment_attempt_id,
                        principalTable: "payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reconciliation_exceptions_refunds_refund_id",
                        column: x => x.refund_id,
                        principalTable: "refunds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_exceptions_booking_id",
                table: "reconciliation_exceptions",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_exceptions_gateway_event_id",
                table: "reconciliation_exceptions",
                column: "gateway_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_exceptions_payment_attempt_id",
                table: "reconciliation_exceptions",
                column: "payment_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_exceptions_refund_id",
                table: "reconciliation_exceptions",
                column: "refund_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_exceptions_resolved_by_user_id",
                table: "reconciliation_exceptions",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_status_seen",
                table: "reconciliation_exceptions",
                columns: new[] { "status", "last_seen_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_reconciliation_fingerprint",
                table: "reconciliation_exceptions",
                column: "fingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reconciliation_exceptions");
        }
    }
}
