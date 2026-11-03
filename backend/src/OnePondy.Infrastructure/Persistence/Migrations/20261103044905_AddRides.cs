using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ride_driver_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    vehicle_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    vehicle_number = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    licence_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    permit_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    insurance_policy_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    insurance_valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                    approval_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_on_duty = table.Column<bool>(type: "boolean", nullable: false),
                    on_duty_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_latitude = table.Column<double>(type: "double precision", nullable: true),
                    last_longitude = table.Column<double>(type: "double precision", nullable: true),
                    last_location_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ride_driver_profiles", x => x.user_id);
                    table.CheckConstraint("ck_ride_driver_approval_status", "approval_status IN ('pending', 'approved', 'rejected', 'suspended')");
                    table.CheckConstraint("ck_ride_driver_duty_requires_approval", "NOT is_on_duty OR approval_status = 'approved'");
                    table.CheckConstraint("ck_ride_driver_duty_since", "is_on_duty = (on_duty_since IS NOT NULL)");
                    table.CheckConstraint("ck_ride_driver_location_complete", "(last_latitude IS NULL) = (last_longitude IS NULL) AND (last_latitude IS NULL) = (last_location_at IS NULL)");
                    table.CheckConstraint("ck_ride_driver_location_only_on_duty", "is_on_duty OR (last_latitude IS NULL AND last_longitude IS NULL AND last_location_at IS NULL)");
                    table.CheckConstraint("ck_ride_driver_location_range", "(last_latitude IS NULL OR last_latitude BETWEEN -90 AND 90) AND (last_longitude IS NULL OR last_longitude BETWEEN -180 AND 180)");
                    table.CheckConstraint("ck_ride_driver_review_note", "approval_status NOT IN ('rejected', 'suspended') OR review_note IS NOT NULL");
                    table.CheckConstraint("ck_ride_driver_vehicle_class", "vehicle_class IN ('auto', 'cab', 'bike_taxi')");
                    table.ForeignKey(
                        name: "fk_ride_driver_profiles_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ride_quotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rider_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pickup_latitude = table.Column<double>(type: "double precision", nullable: false),
                    pickup_longitude = table.Column<double>(type: "double precision", nullable: false),
                    pickup_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    destination_latitude = table.Column<double>(type: "double precision", nullable: false),
                    destination_longitude = table.Column<double>(type: "double precision", nullable: false),
                    destination_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    distance_km = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: false),
                    fare = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cancellation_terms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ride_quotes", x => x.id);
                    table.CheckConstraint("ck_ride_quote_amounts", "fare > 0 AND distance_km > 0");
                    table.CheckConstraint("ck_ride_quote_coordinates", "pickup_latitude BETWEEN -90 AND 90 AND pickup_longitude BETWEEN -180 AND 180 AND destination_latitude BETWEEN -90 AND 90 AND destination_longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_ride_quote_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_ride_quote_vehicle_class", "vehicle_class IN ('auto', 'cab', 'bike_taxi')");
                    table.ForeignKey(
                        name: "fk_ride_quotes_app_users_rider_user_id",
                        column: x => x.rider_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rider_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pickup_latitude = table.Column<double>(type: "double precision", nullable: false),
                    pickup_longitude = table.Column<double>(type: "double precision", nullable: false),
                    pickup_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    destination_latitude = table.Column<double>(type: "double precision", nullable: false),
                    destination_longitude = table.Column<double>(type: "double precision", nullable: false),
                    destination_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pickup_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    distance_km = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: false),
                    fare = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cancellation_terms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    driver_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    search_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    arrived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cash_collected = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    driver_latitude = table.Column<double>(type: "double precision", nullable: true),
                    driver_longitude = table.Column<double>(type: "double precision", nullable: true),
                    driver_location_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rides", x => x.id);
                    table.CheckConstraint("ck_ride_amounts", "fare > 0 AND distance_km > 0 AND (cash_collected IS NULL OR cash_collected = fare)");
                    table.CheckConstraint("ck_ride_cancelled_by", "cancelled_by IS NULL OR cancelled_by IN ('rider', 'driver', 'system')");
                    table.CheckConstraint("ck_ride_coordinates", "pickup_latitude BETWEEN -90 AND 90 AND pickup_longitude BETWEEN -180 AND 180 AND destination_latitude BETWEEN -90 AND 90 AND destination_longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_ride_driver", "(status = 'requested' AND driver_user_id IS NULL) OR (status IN ('accepted', 'arrived', 'in_progress', 'completed') AND driver_user_id IS NOT NULL) OR status IN ('cancelled', 'expired')");
                    table.CheckConstraint("ck_ride_location_complete", "(driver_latitude IS NULL) = (driver_longitude IS NULL) AND (driver_latitude IS NULL) = (driver_location_at IS NULL)");
                    table.CheckConstraint("ck_ride_location_only_while_active", "status IN ('accepted', 'arrived', 'in_progress') OR (driver_latitude IS NULL AND driver_longitude IS NULL AND driver_location_at IS NULL)");
                    table.CheckConstraint("ck_ride_location_range", "(driver_latitude IS NULL OR driver_latitude BETWEEN -90 AND 90) AND (driver_longitude IS NULL OR driver_longitude BETWEEN -180 AND 180)");
                    table.CheckConstraint("ck_ride_status", "status IN ('requested', 'accepted', 'arrived', 'in_progress', 'completed', 'cancelled', 'expired')");
                    table.CheckConstraint("ck_ride_times", "(status NOT IN ('accepted', 'arrived', 'in_progress', 'completed') OR accepted_at IS NOT NULL) AND (status NOT IN ('arrived', 'in_progress', 'completed') OR arrived_at IS NOT NULL) AND (status NOT IN ('in_progress', 'completed') OR started_at IS NOT NULL) AND (status <> 'completed' OR (completed_at IS NOT NULL AND cash_collected IS NOT NULL)) AND (status NOT IN ('cancelled', 'expired') OR (cancelled_at IS NOT NULL AND cancelled_by IS NOT NULL)) AND (status IN ('cancelled', 'expired') OR cancelled_at IS NULL)");
                    table.CheckConstraint("ck_ride_vehicle_class", "vehicle_class IN ('auto', 'cab', 'bike_taxi')");
                    table.ForeignKey(
                        name: "fk_rides_app_users_driver_user_id",
                        column: x => x.driver_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rides_app_users_rider_user_id",
                        column: x => x.rider_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rides_ride_quotes_quote_id",
                        column: x => x.quote_id,
                        principalTable: "ride_quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ride_driver_profiles_status_duty",
                table: "ride_driver_profiles",
                columns: new[] { "approval_status", "is_on_duty" });

            migrationBuilder.CreateIndex(
                name: "ux_ride_driver_profiles_licence",
                table: "ride_driver_profiles",
                column: "licence_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ride_driver_profiles_vehicle",
                table: "ride_driver_profiles",
                column: "vehicle_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ride_quotes_rider_created",
                table: "ride_quotes",
                columns: new[] { "rider_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rides_rider_requested",
                table: "rides",
                columns: new[] { "rider_user_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rides_status_class_requested",
                table: "rides",
                columns: new[] { "status", "vehicle_class", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_rides_one_active_per_driver",
                table: "rides",
                column: "driver_user_id",
                unique: true,
                filter: "status IN ('accepted', 'arrived', 'in_progress')");

            migrationBuilder.CreateIndex(
                name: "ux_rides_one_open_per_rider",
                table: "rides",
                column: "rider_user_id",
                unique: true,
                filter: "status IN ('requested', 'accepted', 'arrived', 'in_progress')");

            migrationBuilder.CreateIndex(
                name: "ux_rides_quote",
                table: "rides",
                column: "quote_id",
                unique: true);

            // The rules the application follows, repeated where nothing can skip them: a ride moves only along the allowed steps, a taken ride can
            // never be handed to a second driver (so two drivers accepting at the same moment cannot both get it), a finished ride never changes,
            // and rides are kept (a ride is a record of a trip, and the safety and dispute trail depends on it).
            migrationBuilder.Sql("""
                CREATE FUNCTION ride_follows_the_rules() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
                        RAISE EXCEPTION 'rides are kept: cancel the ride instead';
                    END IF;
                    IF NEW.rider_user_id <> OLD.rider_user_id OR NEW.quote_id <> OLD.quote_id OR NEW.fare <> OLD.fare THEN
                        RAISE EXCEPTION 'a ride''s rider, quote and fare cannot change';
                    END IF;
                    IF OLD.status IN ('completed', 'cancelled', 'expired') THEN
                        RAISE EXCEPTION 'a % ride cannot change', OLD.status;
                    END IF;
                    IF NEW.status <> OLD.status AND NOT (
                        (OLD.status = 'requested' AND NEW.status IN ('accepted', 'cancelled', 'expired'))
                        OR (OLD.status = 'accepted' AND NEW.status IN ('arrived', 'requested', 'cancelled'))
                        OR (OLD.status = 'arrived' AND NEW.status IN ('in_progress', 'cancelled'))
                        OR (OLD.status = 'in_progress' AND NEW.status = 'completed')) THEN
                        RAISE EXCEPTION 'a ride cannot go from % to %', OLD.status, NEW.status;
                    END IF;
                    IF OLD.status IN ('accepted', 'arrived', 'in_progress') AND NEW.status IN ('accepted', 'arrived', 'in_progress')
                       AND NEW.driver_user_id IS DISTINCT FROM OLD.driver_user_id THEN
                        RAISE EXCEPTION 'this ride already belongs to another driver';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_rides_rules BEFORE UPDATE OR DELETE ON rides
                    FOR EACH ROW EXECUTE FUNCTION ride_follows_the_rules();
                CREATE TRIGGER trg_rides_no_truncate BEFORE TRUNCATE ON rides
                    FOR EACH STATEMENT EXECUTE FUNCTION ride_follows_the_rules();
                """);

            // The customer's one list of everything they have bought now includes rides (spec Phase 9). Dropped and recreated rather than replaced:
            // a union can change a column's length, which REPLACE refuses.
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
                JOIN businesses r ON r.id = o.business_id
                UNION ALL
                SELECT rd.id,
                       'ride'::varchar(16) AS kind,
                       rd.rider_user_id AS customer_user_id,
                       (rd.pickup_label || ' to ' || rd.destination_label)::varchar(404) AS title,
                       rd.status,
                       COALESCE(rd.started_at, rd.requested_at) AS scheduled_at,
                       rd.fare AS total_amount,
                       rd.currency,
                       rd.requested_at AS created_at
                FROM rides rd;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.DropTable(
                name: "ride_driver_profiles");

            migrationBuilder.DropTable(
                name: "rides");

            migrationBuilder.DropTable(
                name: "ride_quotes");

            migrationBuilder.Sql("DROP FUNCTION ride_follows_the_rules();");
        }
    }
}
