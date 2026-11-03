using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveRideDriverPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ride_driver_positions",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ride_driver_positions", x => x.user_id);
                    table.CheckConstraint("ck_ride_driver_position_range", "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_ride_driver_positions_ride_driver_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "ride_driver_profiles",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ride_driver_positions_recorded",
                table: "ride_driver_positions",
                column: "recorded_at");

            // Positions already reported move across before the old columns go, so nobody on duty drops off the map.
            migrationBuilder.Sql("""
                INSERT INTO ride_driver_positions (user_id, latitude, longitude, recorded_at)
                SELECT user_id, last_latitude, last_longitude, last_location_at
                FROM ride_driver_profiles
                WHERE is_on_duty AND last_latitude IS NOT NULL AND last_longitude IS NOT NULL AND last_location_at IS NOT NULL;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_driver_location_complete",
                table: "ride_driver_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_driver_location_only_on_duty",
                table: "ride_driver_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_driver_location_range",
                table: "ride_driver_profiles");

            migrationBuilder.DropColumn(
                name: "last_latitude",
                table: "ride_driver_profiles");

            migrationBuilder.DropColumn(
                name: "last_location_at",
                table: "ride_driver_profiles");

            migrationBuilder.DropColumn(
                name: "last_longitude",
                table: "ride_driver_profiles");

            // The rule the old check constraint kept, now across two tables: a position exists only while its driver is on duty. The profile erases
            // it the moment the shift ends, and the position table refuses one for a driver who is not on duty.
            migrationBuilder.Sql("""
                CREATE FUNCTION ride_position_needs_duty() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM ride_driver_profiles WHERE user_id = NEW.user_id AND is_on_duty) THEN
                        RAISE EXCEPTION 'a position is kept only while the driver is on duty';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_ride_driver_positions_on_duty BEFORE INSERT OR UPDATE ON ride_driver_positions
                    FOR EACH ROW EXECUTE FUNCTION ride_position_needs_duty();

                CREATE FUNCTION ride_profile_erases_position() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    DELETE FROM ride_driver_positions WHERE user_id = NEW.user_id;
                    RETURN NULL;
                END;
                $$;
                CREATE TRIGGER trg_ride_driver_profiles_off_duty AFTER UPDATE OF is_on_duty ON ride_driver_profiles
                    FOR EACH ROW WHEN (OLD.is_on_duty AND NOT NEW.is_on_duty) EXECUTE FUNCTION ride_profile_erases_position();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ride_driver_profiles_off_duty ON ride_driver_profiles; DROP FUNCTION IF EXISTS ride_profile_erases_position();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ride_driver_positions_on_duty ON ride_driver_positions; DROP FUNCTION IF EXISTS ride_position_needs_duty();");

            migrationBuilder.DropTable(
                name: "ride_driver_positions");

            migrationBuilder.AddColumn<double>(
                name: "last_latitude",
                table: "ride_driver_profiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_location_at",
                table: "ride_driver_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "last_longitude",
                table: "ride_driver_profiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_driver_location_complete",
                table: "ride_driver_profiles",
                sql: "(last_latitude IS NULL) = (last_longitude IS NULL) AND (last_latitude IS NULL) = (last_location_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_driver_location_only_on_duty",
                table: "ride_driver_profiles",
                sql: "is_on_duty OR (last_latitude IS NULL AND last_longitude IS NULL AND last_location_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_driver_location_range",
                table: "ride_driver_profiles",
                sql: "(last_latitude IS NULL OR last_latitude BETWEEN -90 AND 90) AND (last_longitude IS NULL OR last_longitude BETWEEN -180 AND 180)");
        }
    }
}
