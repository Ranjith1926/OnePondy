using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddListingsAndBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "listings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_bookable = table.Column<bool>(type: "boolean", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    cancellation_policy = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listings", x => x.id);
                    table.UniqueConstraint("ux_listings_id_business", x => new { x.id, x.business_id });
                    table.CheckConstraint("ck_listing_bookable_policy", "NOT is_bookable OR cancellation_policy IS NOT NULL");
                    table.CheckConstraint("ck_listing_bookable_type", "NOT is_bookable OR listing_type <> 'shop'");
                    table.CheckConstraint("ck_listing_price", "base_price IS NULL OR base_price >= 0");
                    table.CheckConstraint("ck_listing_status", "status IN ('draft', 'pending', 'published', 'suspended')");
                    table.CheckConstraint("ck_listing_type", "listing_type IN ('activity', 'stay', 'event', 'service', 'shop')");
                    table.ForeignKey(
                        name: "fk_listings_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "availability_slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    reserved_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability_slots", x => x.id);
                    table.CheckConstraint("ck_slot_capacity", "capacity >= 0 AND reserved_count >= 0 AND reserved_count <= capacity");
                    table.CheckConstraint("ck_slot_dates", "end_at > start_at");
                    table.ForeignKey(
                        name: "fk_availability_slots_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    guest_count = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    fees = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    price_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    cancellation_policy_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_booking_amounts", "subtotal >= 0 AND fees >= 0 AND tax_amount >= 0 AND discount_amount >= 0 AND total_amount >= 0");
                    table.CheckConstraint("ck_booking_dates", "end_at IS NULL OR start_at IS NULL OR end_at > start_at");
                    table.CheckConstraint("ck_booking_guest_count", "guest_count > 0");
                    table.CheckConstraint("ck_booking_hold", "status NOT IN ('pending', 'awaiting_payment') OR expires_at IS NOT NULL");
                    table.CheckConstraint("ck_booking_status", "status IN ('pending', 'awaiting_payment', 'confirmed', 'failed', 'expired', 'rejected', 'cancelled', 'refund_pending', 'refunded', 'completed')");
                    table.CheckConstraint("ck_booking_total", "total_amount = subtotal + fees + tax_amount - discount_amount");
                    table.CheckConstraint("ck_booking_type", "booking_type IN ('activity', 'stay', 'event', 'service')");
                    table.ForeignKey(
                        name: "fk_bookings_listings_listing_id_business_id",
                        columns: x => new { x.listing_id, x.business_id },
                        principalTable: "listings",
                        principalColumns: new[] { "id", "business_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saved_listings",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_listings", x => new { x.user_id, x.listing_id });
                    table.ForeignKey(
                        name: "fk_saved_listings_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saved_listings_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "booking_slots",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_slots", x => new { x.booking_id, x.slot_id });
                    table.CheckConstraint("ck_booking_slot_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_booking_slots_availability_slots_slot_id",
                        column: x => x.slot_id,
                        principalTable: "availability_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_booking_slots_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "booking_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    new_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    changed_by_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_status_history", x => x.id);
                    table.CheckConstraint("ck_booking_history_actor", "(changed_by_type = 'system') = (changed_by_user_id IS NULL)");
                    table.CheckConstraint("ck_booking_history_actor_type", "changed_by_type IN ('customer', 'partner', 'admin', 'system')");
                    table.ForeignKey(
                        name: "fk_booking_status_history_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_slot_listing_start",
                table: "availability_slots",
                columns: new[] { "listing_id", "start_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_booking_slots_slot_id",
                table: "booking_slots",
                column: "slot_id");

            migrationBuilder.CreateIndex(
                name: "ix_booking_status_history_booking_time",
                table: "booking_status_history",
                columns: new[] { "booking_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_business_status",
                table: "bookings",
                columns: new[] { "business_id", "status", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_created",
                table: "bookings",
                columns: new[] { "customer_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_hold_expiry",
                table: "bookings",
                column: "expires_at",
                filter: "status IN ('pending', 'awaiting_payment')");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_listing_id_business_id",
                table: "bookings",
                columns: new[] { "listing_id", "business_id" });

            migrationBuilder.CreateIndex(
                name: "ux_bookings_idempotency",
                table: "bookings",
                columns: new[] { "customer_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listings_business_status",
                table: "listings",
                columns: new[] { "business_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_saved_listings_listing_id",
                table: "saved_listings",
                column: "listing_id");

            // The Bookings tab's read model (spec §17.4); later migrations add UNION ALL branches for
            // food orders (Phase 8) and rides (Phase 9).
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE VIEW customer_history AS
                SELECT b.id,
                       'booking'::varchar(16) AS kind,
                       b.customer_user_id,
                       l.title,
                       b.status,
                       b.start_at AS scheduled_at,
                       b.total_amount,
                       b.currency,
                       b.created_at
                FROM bookings b
                JOIN listings l ON l.id = b.listing_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS customer_history;");

            migrationBuilder.DropTable(
                name: "booking_slots");

            migrationBuilder.DropTable(
                name: "booking_status_history");

            migrationBuilder.DropTable(
                name: "saved_listings");

            migrationBuilder.DropTable(
                name: "availability_slots");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "listings");
        }
    }
}
