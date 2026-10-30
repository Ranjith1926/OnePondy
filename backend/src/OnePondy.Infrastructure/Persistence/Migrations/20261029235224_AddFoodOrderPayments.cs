using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFoodOrderPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_account",
                table: "ledger_entries");

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "settlement_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "food_order_id",
                table: "settlement_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "refunds",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "food_order_id",
                table: "refunds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "food_order_id",
                table: "reconciliation_exceptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "payment_attempts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "food_order_id",
                table: "payment_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "food_order_id",
                table: "ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_settlement_items_food_order_kind",
                table: "settlement_items",
                columns: new[] { "food_order_id", "kind" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_settlement_item_subject",
                table: "settlement_items",
                sql: "num_nonnulls(booking_id, food_order_id) = 1");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_food_order",
                table: "refunds",
                column: "food_order_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_subject",
                table: "refunds",
                sql: "num_nonnulls(booking_id, food_order_id) = 1");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_exceptions_food_order_id",
                table: "reconciliation_exceptions",
                column: "food_order_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reconciliation_subject",
                table: "reconciliation_exceptions",
                sql: "num_nonnulls(booking_id, food_order_id) <= 1");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_food_order",
                table: "payment_attempts",
                columns: new[] { "food_order_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_open_food_order",
                table: "payment_attempts",
                column: "food_order_id",
                unique: true,
                filter: "status = 'created'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempt_subject",
                table: "payment_attempts",
                sql: "num_nonnulls(booking_id, food_order_id) = 1");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_food_order",
                table: "ledger_entries",
                column: "food_order_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_account",
                table: "ledger_entries",
                sql: "account ~ '^(gateway_clearing|customer_refunds|platform_commission|delivery_fees|tax_payable|(partner_payable|partner_paid):[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_subject",
                table: "ledger_entries",
                sql: "num_nonnulls(booking_id, food_order_id) <= 1");

            migrationBuilder.AddForeignKey(
                name: "fk_ledger_entries_food_orders_food_order_id",
                table: "ledger_entries",
                column: "food_order_id",
                principalTable: "food_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payment_attempts_food_orders_food_order_id",
                table: "payment_attempts",
                column: "food_order_id",
                principalTable: "food_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_reconciliation_exceptions_food_orders_food_order_id",
                table: "reconciliation_exceptions",
                column: "food_order_id",
                principalTable: "food_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_food_orders_food_order_id",
                table: "refunds",
                column: "food_order_id",
                principalTable: "food_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_settlement_items_food_orders_food_order_id",
                table: "settlement_items",
                column: "food_order_id",
                principalTable: "food_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // A payment is for one booking or food order for good, and a refund gives money back for what its payment paid for: the
            // refund's copy of the booking or order must be its payment's. (Only new columns were added above: ledger_entries is
            // append-only, so nothing here rewrites an existing row.)
            migrationBuilder.Sql("""
                CREATE FUNCTION payment_attempt_keeps_its_subject() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.booking_id IS DISTINCT FROM OLD.booking_id OR NEW.food_order_id IS DISTINCT FROM OLD.food_order_id THEN
                        RAISE EXCEPTION 'payment attempt % cannot change what it pays for', OLD.id;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_payment_attempts_keep_subject BEFORE UPDATE OF booking_id, food_order_id ON payment_attempts
                    FOR EACH ROW EXECUTE FUNCTION payment_attempt_keeps_its_subject();

                CREATE FUNCTION refund_matches_its_payment() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM payment_attempts
                        WHERE id = NEW.payment_attempt_id
                          AND booking_id IS NOT DISTINCT FROM NEW.booking_id
                          AND food_order_id IS NOT DISTINCT FROM NEW.food_order_id) THEN
                        RAISE EXCEPTION 'refund % is not for what payment % paid for', NEW.id, NEW.payment_attempt_id;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_refunds_match_payment BEFORE INSERT OR UPDATE OF payment_attempt_id, booking_id, food_order_id ON refunds
                    FOR EACH ROW EXECUTE FUNCTION refund_matches_its_payment();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only possible before any food order has been paid: rows for food orders cannot go back into booking-only columns.
            migrationBuilder.Sql("""
                DROP TRIGGER trg_refunds_match_payment ON refunds;
                DROP FUNCTION refund_matches_its_payment();
                DROP TRIGGER trg_payment_attempts_keep_subject ON payment_attempts;
                DROP FUNCTION payment_attempt_keeps_its_subject();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_ledger_entries_food_orders_food_order_id",
                table: "ledger_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_payment_attempts_food_orders_food_order_id",
                table: "payment_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_reconciliation_exceptions_food_orders_food_order_id",
                table: "reconciliation_exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_refunds_food_orders_food_order_id",
                table: "refunds");

            migrationBuilder.DropForeignKey(
                name: "fk_settlement_items_food_orders_food_order_id",
                table: "settlement_items");

            migrationBuilder.DropIndex(
                name: "ux_settlement_items_food_order_kind",
                table: "settlement_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settlement_item_subject",
                table: "settlement_items");

            migrationBuilder.DropIndex(
                name: "ix_refunds_food_order",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_subject",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "ix_reconciliation_exceptions_food_order_id",
                table: "reconciliation_exceptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reconciliation_subject",
                table: "reconciliation_exceptions");

            migrationBuilder.DropIndex(
                name: "ix_payment_attempts_food_order",
                table: "payment_attempts");

            migrationBuilder.DropIndex(
                name: "ux_payment_attempts_open_food_order",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempt_subject",
                table: "payment_attempts");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_food_order",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_account",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_subject",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "food_order_id",
                table: "settlement_items");

            migrationBuilder.DropColumn(
                name: "food_order_id",
                table: "refunds");

            migrationBuilder.DropColumn(
                name: "food_order_id",
                table: "reconciliation_exceptions");

            migrationBuilder.DropColumn(
                name: "food_order_id",
                table: "payment_attempts");

            migrationBuilder.DropColumn(
                name: "food_order_id",
                table: "ledger_entries");

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "settlement_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "refunds",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "payment_attempts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_account",
                table: "ledger_entries",
                sql: "account ~ '^(gateway_clearing|customer_refunds|platform_commission|(partner_payable|partner_paid):[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$'");
        }
    }
}
