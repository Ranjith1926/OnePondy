using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: true),
                    topic = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    food_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalation_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    first_response_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_requester_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_staff_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_tickets", x => x.id);
                    table.CheckConstraint("ck_support_ticket_about", "booking_id IS NULL OR food_order_id IS NULL");
                    table.CheckConstraint("ck_support_ticket_assigned", "(assigned_to_user_id IS NULL) = (assigned_at IS NULL)");
                    table.CheckConstraint("ck_support_ticket_escalated", "((escalated_at IS NULL) = (escalation_reason IS NULL)) AND (escalation_reason IS NULL OR char_length(escalation_reason) <= 200)");
                    table.CheckConstraint("ck_support_ticket_resolved", "((status = 'resolved') = (resolved_at IS NOT NULL)) AND ((resolved_at IS NULL) = (resolved_by_user_id IS NULL))");
                    table.CheckConstraint("ck_support_ticket_status", "status IN ('open', 'waiting_for_requester', 'resolved')");
                    table.CheckConstraint("ck_support_ticket_subject", "char_length(subject) BETWEEN 1 AND 120");
                    table.CheckConstraint("ck_support_ticket_topic", "topic IN ('booking', 'food_order', 'payment', 'account', 'business', 'other')");
                    table.ForeignKey(
                        name: "fk_support_tickets_app_users_assigned_to_user_id",
                        column: x => x.assigned_to_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_tickets_app_users_requester_user_id",
                        column: x => x.requester_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_tickets_app_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_tickets_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_tickets_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_tickets_food_orders_food_order_id",
                        column: x => x.food_order_id,
                        principalTable: "food_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_messages", x => x.id);
                    table.CheckConstraint("ck_support_message_author_kind", "author_kind IN ('requester', 'staff')");
                    table.CheckConstraint("ck_support_message_body", "char_length(body) BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "fk_support_messages_app_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_messages_support_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_support_messages_author_user_id",
                table: "support_messages",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_messages_ticket_time",
                table: "support_messages",
                columns: new[] { "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_assigned",
                table: "support_tickets",
                columns: new[] { "assigned_to_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_booking_id",
                table: "support_tickets",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_business_id",
                table: "support_tickets",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_food_order_id",
                table: "support_tickets",
                column: "food_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_requester",
                table: "support_tickets",
                columns: new[] { "requester_user_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_resolved_by_user_id",
                table: "support_tickets",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_status_waiting",
                table: "support_tickets",
                columns: new[] { "status", "last_requester_message_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_messages");

            migrationBuilder.DropTable(
                name: "support_tickets");
        }
    }
}
