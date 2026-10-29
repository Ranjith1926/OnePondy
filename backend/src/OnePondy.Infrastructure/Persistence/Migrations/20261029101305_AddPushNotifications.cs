using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dedupe_key",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "outbox_messages",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "device_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_tokens", x => x.id);
                    table.CheckConstraint("ck_device_token_platform", "platform IN ('android', 'ios')");
                    table.ForeignKey(
                        name: "fk_device_tokens_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_device_tokens_user_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "user_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_ticket_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    receipt_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_log", x => x.id);
                    table.CheckConstraint("ck_notification_log_sent_has_ticket", "status <> 'sent' OR provider_ticket_id IS NOT NULL");
                    table.CheckConstraint("ck_notification_log_status", "status IN ('sent', 'delivered', 'failed')");
                    table.ForeignKey(
                        name: "fk_notification_log_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notification_log_device_tokens_device_token_id",
                        column: x => x.device_token_id,
                        principalTable: "device_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_notification_log_outbox_messages_outbox_message_id",
                        column: x => x.outbox_message_id,
                        principalTable: "outbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_outbox_dedupe",
                table: "outbox_messages",
                column: "dedupe_key",
                unique: true,
                filter: "dedupe_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_session",
                table: "device_tokens",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_user",
                table: "device_tokens",
                columns: new[] { "user_id", "last_seen_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_device_tokens_token",
                table: "device_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_log_awaiting_receipt",
                table: "notification_log",
                column: "created_at",
                filter: "status = 'sent' AND receipt_checked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notification_log_device_token_id",
                table: "notification_log",
                column: "device_token_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_log_message",
                table: "notification_log",
                column: "outbox_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_log_user_id",
                table: "notification_log",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_log");

            migrationBuilder.DropTable(
                name: "device_tokens");

            migrationBuilder.DropIndex(
                name: "ux_outbox_dedupe",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "dedupe_key",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "outbox_messages");
        }
    }
}
