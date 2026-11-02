using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashOnDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_account",
                table: "ledger_entries");

            migrationBuilder.AddColumn<bool>(
                name: "accepts_cash_on_delivery",
                table: "restaurant_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_collected",
                table: "food_orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cash_collected_at",
                table: "food_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cash_collected_by",
                table: "food_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                table: "food_orders",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "online");

            migrationBuilder.CreateTable(
                name: "driver_cash_deposits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_cash_deposits", x => x.id);
                    table.CheckConstraint("ck_driver_cash_deposit_amount", "amount > 0");
                    table.CheckConstraint("ck_driver_cash_deposit_times", "received_at <= recorded_at");
                    table.ForeignKey(
                        name: "fk_driver_cash_deposits_app_users_driver_user_id",
                        column: x => x.driver_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_driver_cash_deposits_app_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // A deposit is a record of cash handed in: it is never changed or removed, and a mistake is answered by an administrator's note.
            migrationBuilder.Sql("""
                CREATE FUNCTION driver_cash_deposits_are_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'driver cash deposits are never changed or removed';
                END;
                $$;
                CREATE TRIGGER trg_driver_cash_deposits_append_only BEFORE UPDATE OR DELETE ON driver_cash_deposits
                    FOR EACH ROW EXECUTE FUNCTION driver_cash_deposits_are_immutable();
                CREATE TRIGGER trg_driver_cash_deposits_no_truncate BEFORE TRUNCATE ON driver_cash_deposits
                    FOR EACH STATEMENT EXECUTE FUNCTION driver_cash_deposits_are_immutable();
                """);

            // A driver cannot hand in more cash than they hold: their account never goes below nothing. Checked when the transaction commits,
            // under a lock on the account, so two administrators recording a deposit at the same moment cannot both succeed (the second sees the first).
            migrationBuilder.Sql("""
                CREATE FUNCTION driver_cash_never_negative() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    total numeric;
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtext(NEW.account));
                    SELECT COALESCE(SUM(amount), 0) INTO total FROM ledger_entries WHERE account = NEW.account;
                    IF total < 0 THEN
                        RAISE EXCEPTION 'driver cash account % would go below nothing', NEW.account;
                    END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER trg_driver_cash_never_negative AFTER INSERT ON ledger_entries
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.account LIKE 'driver\_cash:%')
                    EXECUTE FUNCTION driver_cash_never_negative();
                """);

            migrationBuilder.InsertData(
                table: "module_flags",
                columns: new[] { "key", "status", "updated_at", "updated_by_user_id" },
                values: new object[] { "cashOnDelivery", "coming_soon", null, null });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_account",
                table: "ledger_entries",
                sql: "account ~ '^(gateway_clearing|customer_refunds|platform_commission|delivery_fees|tax_payable|cash_received|(partner_payable|partner_paid|driver_cash):[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$'");

            migrationBuilder.CreateIndex(
                name: "ix_food_orders_cash_collected_by",
                table: "food_orders",
                column: "cash_collected_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_food_order_cash",
                table: "food_orders",
                sql: "((cash_collected IS NULL) = (cash_collected_at IS NULL)) AND ((cash_collected IS NULL) = (cash_collected_by IS NULL)) AND (cash_collected IS NULL OR (payment_method = 'cash_on_delivery' AND status = 'delivered' AND cash_collected = total_amount)) AND NOT (payment_method = 'cash_on_delivery' AND status = 'delivered' AND cash_collected IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_food_order_payment_method",
                table: "food_orders",
                sql: "payment_method IN ('online', 'cash_on_delivery')");

            migrationBuilder.CreateIndex(
                name: "ix_driver_cash_deposits_driver",
                table: "driver_cash_deposits",
                columns: new[] { "driver_user_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_driver_cash_deposits_recorded_by_user_id",
                table: "driver_cash_deposits",
                column: "recorded_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_food_orders_app_users_cash_collected_by",
                table: "food_orders",
                column: "cash_collected_by",
                principalTable: "app_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER trg_driver_cash_never_negative ON ledger_entries;
                DROP FUNCTION driver_cash_never_negative();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_food_orders_app_users_cash_collected_by",
                table: "food_orders");

            migrationBuilder.DropTable(
                name: "driver_cash_deposits");

            migrationBuilder.Sql("DROP FUNCTION driver_cash_deposits_are_immutable();");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_account",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_food_orders_cash_collected_by",
                table: "food_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_food_order_cash",
                table: "food_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_food_order_payment_method",
                table: "food_orders");

            migrationBuilder.DeleteData(
                table: "module_flags",
                keyColumn: "key",
                keyValue: "cashOnDelivery");

            migrationBuilder.DropColumn(
                name: "accepts_cash_on_delivery",
                table: "restaurant_profiles");

            migrationBuilder.DropColumn(
                name: "cash_collected",
                table: "food_orders");

            migrationBuilder.DropColumn(
                name: "cash_collected_at",
                table: "food_orders");

            migrationBuilder.DropColumn(
                name: "cash_collected_by",
                table: "food_orders");

            migrationBuilder.DropColumn(
                name: "payment_method",
                table: "food_orders");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_account",
                table: "ledger_entries",
                sql: "account ~ '^(gateway_clearing|customer_refunds|platform_commission|delivery_fees|tax_payable|(partner_payable|partner_paid):[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$'");
        }
    }
}
