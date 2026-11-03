using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRideSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ride_safety_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ride_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reported_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ride_safety_events", x => x.id);
                    table.CheckConstraint("ck_ride_safety_category", "category IN ('emergency', 'unsafe_driving', 'harassment', 'accident', 'vehicle_problem', 'overcharge', 'other')");
                    table.CheckConstraint("ck_ride_safety_kind", "kind IN ('sos', 'incident')");
                    table.CheckConstraint("ck_ride_safety_location", "(latitude IS NULL) = (longitude IS NULL) AND (latitude IS NULL OR latitude BETWEEN -90 AND 90) AND (longitude IS NULL OR longitude BETWEEN -180 AND 180)");
                    table.CheckConstraint("ck_ride_safety_reported_by", "reported_by IN ('rider', 'driver')");
                    table.CheckConstraint("ck_ride_safety_shape", "(kind = 'sos' AND category = 'emergency' AND details IS NULL) OR (kind = 'incident' AND category <> 'emergency' AND details IS NOT NULL AND latitude IS NULL AND longitude IS NULL)");
                    table.CheckConstraint("ck_ride_safety_status", "status IN ('open', 'acknowledged', 'resolved')");
                    table.CheckConstraint("ck_ride_safety_steps", "((acknowledged_by_user_id IS NULL) = (acknowledged_at IS NULL)) AND ((resolved_by_user_id IS NULL) = (resolved_at IS NULL)) AND (status <> 'resolved' OR (resolved_at IS NOT NULL AND resolution_note IS NOT NULL)) AND (status = 'resolved' OR resolved_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_ride_safety_events_app_users_reported_by_user_id",
                        column: x => x.reported_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ride_safety_events_rides_ride_id",
                        column: x => x.ride_id,
                        principalTable: "rides",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ride_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ride_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ride_shares", x => x.id);
                    table.CheckConstraint("ck_ride_share_expiry", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_ride_shares_rides_ride_id",
                        column: x => x.ride_id,
                        principalTable: "rides",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ride_safety_events_queue",
                table: "ride_safety_events",
                columns: new[] { "status", "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ride_safety_events_reported_by_user_id",
                table: "ride_safety_events",
                column: "reported_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ride_safety_events_ride",
                table: "ride_safety_events",
                column: "ride_id");

            migrationBuilder.CreateIndex(
                name: "ux_ride_safety_events_open_sos",
                table: "ride_safety_events",
                columns: new[] { "ride_id", "reported_by_user_id" },
                unique: true,
                filter: "kind = 'sos' AND status <> 'resolved'");

            migrationBuilder.CreateIndex(
                name: "ix_ride_shares_ride",
                table: "ride_shares",
                column: "ride_id");

            migrationBuilder.CreateIndex(
                name: "ux_ride_shares_token",
                table: "ride_shares",
                column: "token_hash",
                unique: true);

            // Safety reports are a record of what was reported and what staff did: they are never deleted, and a closed one never changes.
            migrationBuilder.Sql("""
                CREATE FUNCTION ride_safety_event_is_kept() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
                        RAISE EXCEPTION 'safety reports are kept: resolve the report instead';
                    END IF;
                    IF OLD.status = 'resolved' THEN
                        RAISE EXCEPTION 'a resolved safety report cannot change';
                    END IF;
                    IF NEW.ride_id <> OLD.ride_id OR NEW.kind <> OLD.kind OR NEW.reported_by_user_id <> OLD.reported_by_user_id THEN
                        RAISE EXCEPTION 'a safety report cannot move to another ride or person';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_ride_safety_events_kept BEFORE UPDATE OR DELETE ON ride_safety_events
                    FOR EACH ROW EXECUTE FUNCTION ride_safety_event_is_kept();
                CREATE TRIGGER trg_ride_safety_events_no_truncate BEFORE TRUNCATE ON ride_safety_events
                    FOR EACH STATEMENT EXECUTE FUNCTION ride_safety_event_is_kept();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ride_safety_events");

            migrationBuilder.DropTable(
                name: "ride_shares");

            migrationBuilder.Sql("DROP FUNCTION ride_safety_event_is_kept();");
        }
    }
}
