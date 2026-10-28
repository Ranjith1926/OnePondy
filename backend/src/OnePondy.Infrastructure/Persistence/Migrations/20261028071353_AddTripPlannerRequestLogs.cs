using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripPlannerRequestLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_planner_request_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    day_count = table.Column<int>(type: "integer", nullable: false),
                    interests = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    candidate_count = table.Column<int>(type: "integer", nullable: false),
                    suggested_count = table.Column<int>(type: "integer", nullable: false),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_planner_request_logs", x => x.id);
                    table.CheckConstraint("ck_trip_planner_log_outcome", "outcome IN ('suggested', 'no_candidates', 'provider_unavailable')");
                    table.ForeignKey(
                        name: "fk_trip_planner_request_logs_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trip_planner_request_logs_created_at_user_id",
                table: "trip_planner_request_logs",
                columns: new[] { "created_at", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_planner_request_logs_user_id",
                table: "trip_planner_request_logs",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_planner_request_logs");
        }
    }
}
