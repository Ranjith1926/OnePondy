using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFoodOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "food_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivery_area = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    delivery_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    delivery_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    prep_time_minutes = table.Column<int>(type: "integer", nullable: false),
                    estimated_ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    delivery_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    packaging_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    closed_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food_orders", x => x.id);
                    table.CheckConstraint("ck_food_order_amounts", "subtotal > 0 AND delivery_fee >= 0 AND packaging_fee >= 0 AND tax_amount >= 0 AND discount_amount >= 0 AND total_amount > 0");
                    table.CheckConstraint("ck_food_order_area", "delivery_area IN ('whiteTown', 'tamilQuarter', 'auroville', 'lawspet', 'muthialpet', 'reddiarpalayam', 'ariyankuppam', 'villianur')");
                    table.CheckConstraint("ck_food_order_hold", "status NOT IN ('placed', 'awaiting_payment') OR expires_at IS NOT NULL");
                    table.CheckConstraint("ck_food_order_prep_time", "prep_time_minutes > 0");
                    table.CheckConstraint("ck_food_order_status", "status IN ('placed', 'awaiting_payment', 'confirmed', 'preparing', 'ready_for_pickup', 'out_for_delivery', 'delivered', 'rejected', 'cancelled', 'expired', 'failed', 'refund_pending', 'refunded')");
                    table.CheckConstraint("ck_food_order_total", "total_amount = subtotal + delivery_fee + packaging_fee + tax_amount - discount_amount");
                    table.ForeignKey(
                        name: "fk_food_orders_app_users_customer_user_id",
                        column: x => x.customer_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_food_orders_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "food_order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_veg = table.Column<bool>(type: "boolean", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food_order_lines", x => x.id);
                    table.CheckConstraint("ck_food_order_line_price", "unit_price > 0 AND tax_rate_percent >= 0 AND tax_amount >= 0");
                    table.CheckConstraint("ck_food_order_line_quantity", "quantity BETWEEN 1 AND 20");
                    table.CheckConstraint("ck_food_order_line_subtotal", "subtotal = unit_price * quantity");
                    table.ForeignKey(
                        name: "fk_food_order_lines_food_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "food_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_food_order_lines_menu_items_menu_item_id",
                        column: x => x.menu_item_id,
                        principalTable: "menu_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "food_order_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    new_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    changed_by_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food_order_status_history", x => x.id);
                    table.CheckConstraint("ck_food_order_history_actor", "(changed_by_type = 'system') = (changed_by_user_id IS NULL)");
                    table.CheckConstraint("ck_food_order_history_actor_type", "changed_by_type IN ('customer', 'partner', 'admin', 'system', 'driver')");
                    table.CheckConstraint("ck_food_order_history_status", "new_status IN ('placed', 'awaiting_payment', 'confirmed', 'preparing', 'ready_for_pickup', 'out_for_delivery', 'delivered', 'rejected', 'cancelled', 'expired', 'failed', 'refund_pending', 'refunded') AND (previous_status IS NULL OR previous_status IN ('placed', 'awaiting_payment', 'confirmed', 'preparing', 'ready_for_pickup', 'out_for_delivery', 'delivered', 'rejected', 'cancelled', 'expired', 'failed', 'refund_pending', 'refunded'))");
                    table.ForeignKey(
                        name: "fk_food_order_status_history_app_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_food_order_status_history_food_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "food_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_food_order_lines_menu_item_id",
                table: "food_order_lines",
                column: "menu_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_food_order_lines_dish",
                table: "food_order_lines",
                columns: new[] { "order_id", "menu_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_food_order_status_history_changed_by_user_id",
                table: "food_order_status_history",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_order_status_history_order_time",
                table: "food_order_status_history",
                columns: new[] { "order_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_food_orders_business_status",
                table: "food_orders",
                columns: new[] { "business_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_food_orders_customer_created",
                table: "food_orders",
                columns: new[] { "customer_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_food_orders_expiry",
                table: "food_orders",
                column: "expires_at",
                filter: "status IN ('placed', 'awaiting_payment')");

            migrationBuilder.CreateIndex(
                name: "ux_food_orders_idempotency",
                table: "food_orders",
                columns: new[] { "customer_user_id", "idempotency_key" },
                unique: true);

            // The status history is the audit trail of every order, and a line is the record of what was bought at what price:
            // the database refuses edits and deletes of both, as it does for booking history.
            migrationBuilder.Sql("""
                CREATE FUNCTION food_order_records_are_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION '% is append-only', TG_TABLE_NAME;
                END;
                $$;
                CREATE TRIGGER trg_food_order_status_history_append_only BEFORE UPDATE OR DELETE ON food_order_status_history
                    FOR EACH ROW EXECUTE FUNCTION food_order_records_are_immutable();
                CREATE TRIGGER trg_food_order_status_history_no_truncate BEFORE TRUNCATE ON food_order_status_history
                    FOR EACH STATEMENT EXECUTE FUNCTION food_order_records_are_immutable();
                CREATE TRIGGER trg_food_order_lines_append_only BEFORE UPDATE OR DELETE ON food_order_lines
                    FOR EACH ROW EXECUTE FUNCTION food_order_records_are_immutable();
                CREATE TRIGGER trg_food_order_lines_no_truncate BEFORE TRUNCATE ON food_order_lines
                    FOR EACH STATEMENT EXECUTE FUNCTION food_order_records_are_immutable();
                """);

            // An order's subtotal and tax are the sums of its lines, checked when the transaction commits (so the order and its
            // lines can be written in any order). It also means an order cannot exist without lines.
            migrationBuilder.Sql("""
                CREATE FUNCTION food_order_matches_its_lines() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    target uuid;
                    line_subtotal numeric;
                    line_tax numeric;
                BEGIN
                    IF TG_TABLE_NAME = 'food_orders' THEN target := NEW.id; ELSE target := NEW.order_id; END IF;
                    SELECT COALESCE(SUM(subtotal), 0), COALESCE(SUM(tax_amount), 0) INTO line_subtotal, line_tax
                        FROM food_order_lines WHERE order_id = target;
                    IF NOT EXISTS (SELECT 1 FROM food_orders WHERE id = target AND subtotal = line_subtotal AND tax_amount = line_tax) THEN
                        RAISE EXCEPTION 'food order % does not match the sum of its lines', target;
                    END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER trg_food_orders_match_lines AFTER INSERT ON food_orders
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION food_order_matches_its_lines();
                CREATE CONSTRAINT TRIGGER trg_food_order_lines_match_order AFTER INSERT ON food_order_lines
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION food_order_matches_its_lines();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "food_order_lines");

            migrationBuilder.DropTable(
                name: "food_order_status_history");

            migrationBuilder.DropTable(
                name: "food_orders");

            migrationBuilder.Sql("""
                DROP FUNCTION food_order_matches_its_lines();
                DROP FUNCTION food_order_records_are_immutable();
                """);
        }
    }
}
