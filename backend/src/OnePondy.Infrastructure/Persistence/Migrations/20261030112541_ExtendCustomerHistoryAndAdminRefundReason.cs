using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendCustomerHistoryAndAdminRefundReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_reason",
                table: "refunds");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_reason",
                table: "refunds",
                sql: "reason IN ('rejected', 'partner_cancelled', 'customer_cancelled', 'late_payment', 'admin_cancelled')");

            // One list of everything a customer has bought, now including food orders (spec Phase 8, step 8.9). Nothing in the application reads
            // this view yet, so it is dropped and recreated rather than replaced (a union can change a column's length, which REPLACE refuses).
            migrationBuilder.Sql("""
                DROP VIEW IF EXISTS customer_history;
                CREATE VIEW customer_history AS
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
                JOIN listings l ON l.id = b.listing_id
                UNION ALL
                SELECT o.id,
                       'food_order'::varchar(16) AS kind,
                       o.customer_user_id,
                       r.name AS title,
                       o.status,
                       o.estimated_ready_at AS scheduled_at,
                       o.total_amount,
                       o.currency,
                       o.created_at
                FROM food_orders o
                JOIN businesses r ON r.id = o.business_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_reason",
                table: "refunds");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_reason",
                table: "refunds",
                sql: "reason IN ('rejected', 'partner_cancelled', 'customer_cancelled', 'late_payment')");

            migrationBuilder.Sql("""
                DROP VIEW IF EXISTS customer_history;
                CREATE VIEW customer_history AS
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
    }
}
