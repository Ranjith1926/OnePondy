using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenBookingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_booking_status_history_bookings_booking_id",
                table: "booking_status_history");

            migrationBuilder.DropIndex(
                name: "ix_bookings_customer_created",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_created",
                table: "bookings",
                columns: new[] { "customer_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_booking_status_history_actor_user_id",
                table: "booking_status_history",
                column: "changed_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_booking_history_status",
                table: "booking_status_history",
                sql: "new_status IN ('pending', 'awaiting_payment', 'confirmed', 'failed', 'expired', 'rejected', 'cancelled', 'refund_pending', 'refunded', 'completed') AND (previous_status IS NULL OR previous_status IN ('pending', 'awaiting_payment', 'confirmed', 'failed', 'expired', 'rejected', 'cancelled', 'refund_pending', 'refunded', 'completed'))");

            migrationBuilder.AddForeignKey(
                name: "fk_booking_status_history_app_users_actor_user_id",
                table: "booking_status_history",
                column: "changed_by_user_id",
                principalTable: "app_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Never cascade-deleted (spec §17.3). Kept by hand: EF's model snapshot cannot represent a
            // non-cascade key on an owned collection, so it is not in the model, only here.
            migrationBuilder.AddForeignKey(
                name: "fk_booking_status_history_bookings_booking_id",
                table: "booking_status_history",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_app_users_customer_user_id",
                table: "bookings",
                column: "customer_user_id",
                principalTable: "app_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // The status history is the audit trail of every booking (spec §17.3): the database
            // refuses edits and deletes, the same way it does for the audit log.
            migrationBuilder.Sql("""
                CREATE FUNCTION booking_status_history_is_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'booking_status_history is append-only';
                END;
                $$;
                CREATE TRIGGER trg_booking_status_history_append_only BEFORE UPDATE OR DELETE ON booking_status_history
                    FOR EACH ROW EXECUTE FUNCTION booking_status_history_is_append_only();
                CREATE TRIGGER trg_booking_status_history_no_truncate BEFORE TRUNCATE ON booking_status_history
                    FOR EACH STATEMENT EXECUTE FUNCTION booking_status_history_is_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_booking_status_history_no_truncate ON booking_status_history;
                DROP TRIGGER IF EXISTS trg_booking_status_history_append_only ON booking_status_history;
                DROP FUNCTION IF EXISTS booking_status_history_is_append_only();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_booking_status_history_app_users_actor_user_id",
                table: "booking_status_history");

            migrationBuilder.DropForeignKey(
                name: "fk_booking_status_history_bookings_booking_id",
                table: "booking_status_history");

            migrationBuilder.DropForeignKey(
                name: "fk_bookings_app_users_customer_user_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_customer_created",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_booking_status_history_actor_user_id",
                table: "booking_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_booking_history_status",
                table: "booking_status_history");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_created",
                table: "bookings",
                columns: new[] { "customer_user_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_booking_status_history_bookings_booking_id",
                table: "booking_status_history",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
