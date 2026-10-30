using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "driver_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    vehicle_number = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    licence_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    approval_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_on_duty = table.Column<bool>(type: "boolean", nullable: false),
                    on_duty_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_profiles", x => x.user_id);
                    table.CheckConstraint("ck_driver_approval_status", "approval_status IN ('pending', 'approved', 'rejected', 'suspended')");
                    table.CheckConstraint("ck_driver_documents", "(vehicle_type = 'bicycle' AND vehicle_number IS NULL AND licence_number IS NULL) OR (vehicle_type <> 'bicycle' AND vehicle_number IS NOT NULL AND licence_number IS NOT NULL)");
                    table.CheckConstraint("ck_driver_duty_requires_approval", "NOT is_on_duty OR approval_status = 'approved'");
                    table.CheckConstraint("ck_driver_duty_since", "is_on_duty = (on_duty_since IS NOT NULL)");
                    table.CheckConstraint("ck_driver_review_note", "approval_status NOT IN ('rejected', 'suspended') OR review_note IS NOT NULL");
                    table.CheckConstraint("ck_driver_vehicle_type", "vehicle_type IN ('bicycle', 'scooter', 'motorbike')");
                    table.ForeignKey(
                        name: "fk_driver_profiles_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_driver_profiles_status_duty",
                table: "driver_profiles",
                columns: new[] { "approval_status", "is_on_duty" });

            migrationBuilder.CreateIndex(
                name: "ux_driver_profiles_licence",
                table: "driver_profiles",
                column: "licence_number",
                unique: true,
                filter: "licence_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "driver_profiles");
        }
    }
}
