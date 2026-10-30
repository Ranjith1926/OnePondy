using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    food_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    driver_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    picked_up_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_latitude = table.Column<double>(type: "double precision", nullable: true),
                    last_longitude = table.Column<double>(type: "double precision", nullable: true),
                    last_location_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_jobs", x => x.id);
                    table.CheckConstraint("ck_delivery_job_driver", "(status = 'open' AND driver_user_id IS NULL) OR (status IN ('accepted', 'picked_up', 'delivered') AND driver_user_id IS NOT NULL) OR status = 'cancelled'");
                    table.CheckConstraint("ck_delivery_job_location_complete", "(last_latitude IS NULL) = (last_longitude IS NULL) AND (last_latitude IS NULL) = (last_location_at IS NULL)");
                    table.CheckConstraint("ck_delivery_job_location_only_while_active", "status IN ('accepted', 'picked_up') OR (last_latitude IS NULL AND last_longitude IS NULL AND last_location_at IS NULL)");
                    table.CheckConstraint("ck_delivery_job_location_range", "(last_latitude IS NULL OR last_latitude BETWEEN -90 AND 90) AND (last_longitude IS NULL OR last_longitude BETWEEN -180 AND 180)");
                    table.CheckConstraint("ck_delivery_job_status", "status IN ('open', 'accepted', 'picked_up', 'delivered', 'cancelled')");
                    table.CheckConstraint("ck_delivery_job_times", "(status <> 'open' OR accepted_at IS NULL) AND (status NOT IN ('accepted', 'picked_up', 'delivered') OR accepted_at IS NOT NULL) AND (status NOT IN ('picked_up', 'delivered') OR picked_up_at IS NOT NULL) AND (status <> 'delivered' OR delivered_at IS NOT NULL) AND (status <> 'cancelled' OR cancelled_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_delivery_jobs_app_users_driver_user_id",
                        column: x => x.driver_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_delivery_jobs_food_orders_food_order_id",
                        column: x => x.food_order_id,
                        principalTable: "food_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_jobs_status_opened",
                table: "delivery_jobs",
                columns: new[] { "status", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ux_delivery_jobs_one_active_per_driver",
                table: "delivery_jobs",
                column: "driver_user_id",
                unique: true,
                filter: "status IN ('accepted', 'picked_up')");

            migrationBuilder.CreateIndex(
                name: "ux_delivery_jobs_order",
                table: "delivery_jobs",
                column: "food_order_id",
                unique: true);

            // Orders already waiting for a driver when this migration is applied get their delivery job, so none is left with nobody to take it.
            migrationBuilder.Sql("""
                INSERT INTO delivery_jobs (id, food_order_id, status, opened_at, updated_at)
                SELECT gen_random_uuid(), id, 'open', updated_at, updated_at FROM food_orders WHERE status = 'ready_for_pickup';
                """);

            // The rules the application follows, repeated where nothing can skip them: a job moves only along the allowed steps, a taken
            // job can never be handed to a second driver (so two drivers accepting at once cannot both get it), and jobs are kept.
            migrationBuilder.Sql("""
                CREATE FUNCTION delivery_job_follows_the_rules() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
                        RAISE EXCEPTION 'delivery jobs are kept: cancel the job instead';
                    END IF;
                    IF NEW.food_order_id <> OLD.food_order_id THEN
                        RAISE EXCEPTION 'a delivery job cannot move to another order';
                    END IF;
                    IF OLD.status IN ('delivered', 'cancelled') THEN
                        RAISE EXCEPTION 'a % delivery job cannot change', OLD.status;
                    END IF;
                    IF NEW.status <> OLD.status AND NOT (
                        (OLD.status = 'open' AND NEW.status IN ('accepted', 'cancelled'))
                        OR (OLD.status = 'accepted' AND NEW.status IN ('picked_up', 'open', 'cancelled'))
                        OR (OLD.status = 'picked_up' AND NEW.status IN ('delivered', 'cancelled'))) THEN
                        RAISE EXCEPTION 'a delivery job cannot go from % to %', OLD.status, NEW.status;
                    END IF;
                    IF OLD.status IN ('accepted', 'picked_up') AND NEW.status IN ('accepted', 'picked_up')
                       AND NEW.driver_user_id IS DISTINCT FROM OLD.driver_user_id THEN
                        RAISE EXCEPTION 'this delivery already belongs to another driver';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_delivery_jobs_rules BEFORE UPDATE OR DELETE ON delivery_jobs
                    FOR EACH ROW EXECUTE FUNCTION delivery_job_follows_the_rules();
                CREATE TRIGGER trg_delivery_jobs_no_truncate BEFORE TRUNCATE ON delivery_jobs
                    FOR EACH STATEMENT EXECUTE FUNCTION delivery_job_follows_the_rules();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_jobs");

            migrationBuilder.Sql("DROP FUNCTION delivery_job_follows_the_rules();");
        }
    }
}
